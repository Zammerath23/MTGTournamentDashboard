using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTGTournamentDashboard.Classifier;
using MTGTournamentDashboard.Data;
using MTGTournamentDashboard.Sync.Models;

namespace MTGTournamentDashboard.Sync.Melee;

internal sealed partial class MeleeDirectSyncService
{
    private const string SourceKey = "Melee";
    private const string TournamentUrlTemplate = "https://melee.gg/Tournament/View/{0}";
    private const int MaxConcurrentDeckFetches = 4;

    [GeneratedRegex(@"(\d+)\s*-\s*(\d+)(?:\s*-\s*(\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex ScoreRegex();

    private readonly IDbContextFactory<MetaDbContext> _dbFactory;
    private readonly MeleeApiClient _api;
    private readonly SyncService _syncService;
    private readonly ClassifierService _classifier;
    private readonly SyncProgress _progress;
    private readonly SyncOptions _options;
    private readonly ILogger<MeleeDirectSyncService> _logger;

    public MeleeDirectSyncService(
        IDbContextFactory<MetaDbContext> dbFactory,
        MeleeApiClient api,
        SyncService syncService,
        ClassifierService classifier,
        SyncProgress progress,
        IOptions<SyncOptions> options,
        ILogger<MeleeDirectSyncService> logger)
    {
        _dbFactory = dbFactory;
        _api = api;
        _syncService = syncService;
        _classifier = classifier;
        _progress = progress;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(int daysBack, CancellationToken ct)
    {
        var endDate = DateTime.UtcNow.Date;
        var startDate = endDate.AddDays(-Math.Max(1, daysBack));

        _progress.BeginRun($"Melee directo: {startDate:yyyy-MM-dd} → {endDate:yyyy-MM-dd}");

        try
        {
            _progress.SetStep("Listando torneos en Melee.gg");
            var all = await _api.ListTournamentsAsync(startDate, endDate, ct);
            _progress.Info($"Total torneos en rango: {all.Count}");

            var format = _options.Formats.FirstOrDefault() ?? "Modern";
            // El endpoint /Decklist/TournamentSearch tiene un bug: marca todo como "Standard" en
            // FormatDescription/Format=0 aunque el torneo sea Modern. Pre-filtramos por nombre
            // (los Modern suelen llevar "Modern" en el título) y validamos formato real leyendo
            // el HTML del torneo (ParseTournamentPage → formats[]).
            var candidates = all
                .Where(t => string.Equals(t.StatusDescription, "Ended", StringComparison.OrdinalIgnoreCase))
                .Where(t => t.Name?.Contains(format, StringComparison.OrdinalIgnoreCase) == true)
                .Where(t => t.Decklists > 0)
                .ToList();
            _progress.Info($"Candidatos {format} (por nombre, terminados, con decklists): {candidates.Count}");

            int skipped = 0, fetched = 0, failed = 0, applied = 0;
            var newDeckIds = new List<int>();

            await using (var dbForExists = await _dbFactory.CreateDbContextAsync(ct))
            {
                foreach (var t in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = string.Format(TournamentUrlTemplate, t.ID);
                    var exists = await dbForExists.Tournaments.AsNoTracking()
                        .AnyAsync(x => x.Source == SourceKey && x.SourceUrl == url, ct);
                    if (exists)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var (item, hash) = await BuildCacheItemAsync(t, format, ct);
                        if (item is null) { skipped++; continue; }

                        await _syncService.ProcessExternalTournamentAsync(
                            sourceKey: SourceKey,
                            relativeIdentifier: $"melee-direct/{t.ID}",
                            item: item,
                            hash: hash,
                            ct: ct);
                        applied++;
                        fetched++;
                        _progress.Info($"Insertado: {t.Name} ({t.ID})");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex, "Fallo procesando torneo Melee {Id}", t.ID);
                        _progress.Warn($"Error en {t.ID}: {ex.Message}");
                    }
                }
            }

            _progress.Info($"Resumen Melee directo: fetched {fetched}, applied {applied}, skipped {skipped}, failed {failed}");

            if (applied > 0)
            {
                await _classifier.RunAsync(reclassifyAll: false, ct);
            }

            _progress.EndRun(success: true);
        }
        catch (OperationCanceledException)
        {
            _progress.EndRun(success: false, error: "Cancelado");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Melee directo falló");
            _progress.EndRun(success: false, error: ex.Message);
            throw;
        }
    }

    private async Task<(CacheItem? item, string hash)> BuildCacheItemAsync(TournamentSearchItem search, string format, CancellationToken ct)
    {
        var html = await _api.GetTournamentHtmlAsync(search.ID, ct);
        var (roundIds, formats) = MeleeHtmlParser.ParseTournamentPage(html);

        if (formats.Length > 0 &&
            !formats.Any(f => string.Equals(f, format, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, "");
        }

        if (roundIds.Length == 0)
        {
            return (null, "");
        }

        var lastRoundId = roundIds[^1];
        var standings = await _api.GetRoundStandingsAsync(lastRoundId, ct);
        if (standings.Count == 0) return (null, "");

        // Sólo decklists declaradas como Modern por el server.
        var playerDeckRefs = standings
            .Where(s => s.Team?.Players is { Length: > 0 } && !string.IsNullOrWhiteSpace(s.Team.Players[0].DisplayName))
            .Select(s => new
            {
                Standing = s,
                PlayerName = NormalizeSpaces(s.Team!.Players![0].DisplayName!),
                DeckIds = (s.Decklists ?? Array.Empty<StandingsDecklist>())
                    .Where(d => !string.IsNullOrWhiteSpace(d.DecklistId) &&
                                string.Equals(d.Format, format, StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.DecklistId!)
                    .ToArray()
            })
            .Where(x => x.DeckIds.Length > 0)
            .ToList();

        if (playerDeckRefs.Count == 0) return (null, "");

        // Descarga los HTML de las decklists con concurrencia limitada.
        var gate = new SemaphoreSlim(MaxConcurrentDeckFetches);
        var fetchTasks = playerDeckRefs.SelectMany(r => r.DeckIds.Select(deckId => Task.Run(async () =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var deckHtml = await _api.GetDeckHtmlAsync(deckId, ct);
                return (PlayerName: r.PlayerName, Standing: r.Standing, DeckId: deckId, Parsed: MeleeHtmlParser.ParseDeckPage(deckHtml));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deck {DeckId} no se pudo cargar", deckId);
                return (PlayerName: r.PlayerName, Standing: r.Standing, DeckId: deckId, Parsed: (MeleeHtmlParser.ParsedDeck?)null);
            }
            finally { gate.Release(); }
        }, ct))).ToList();

        var fetched = await Task.WhenAll(fetchTasks);

        var cacheDecks = new List<CacheDeck>();
        var cacheStandings = new List<CacheStanding>();
        var seenStandingByPlayer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchesByRound = new Dictionary<string, List<CacheMatch>>(StringComparer.OrdinalIgnoreCase);
        var seenMatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in fetched)
        {
            var s = entry.Standing;
            var name = entry.PlayerName;

            if (entry.Parsed is null)
            {
                continue; // Deck no descargable; ignoramos.
            }

            if (seenStandingByPlayer.Add(name))
            {
                cacheStandings.Add(new CacheStanding
                {
                    Rank = s.Rank,
                    Player = name,
                    Points = s.Points,
                    Wins = s.MatchWins,
                    Losses = s.MatchLosses,
                    Draws = s.MatchDraws,
                    OMWP = s.OpponentMatchWinPercentage,
                    GWP = s.TeamGameWinPercentage,
                    OGWP = s.OpponentGameWinPercentage
                });
            }

            cacheDecks.Add(new CacheDeck
            {
                Date = search.StartDate,
                Player = name,
                Result = $"{s.MatchWins}-{s.MatchLosses}-{s.MatchDraws}",
                AnchorUri = new Uri(string.Format(MeleeConstants.DeckPage, entry.DeckId)),
                Mainboard = entry.Parsed.Mainboard,
                Sideboard = entry.Parsed.Sideboard
            });

            // Agregar matches: cada per-round del deck describe un único match (jugador vs oponente).
            // Dedup por (RoundName, sortedPlayers) para que no se inserte dos veces cuando lo
            // veamos desde el otro lado del bracket.
            foreach (var r in entry.Parsed.Rounds)
            {
                if (!TryBuildMatch(name, r.OpponentName, r.ResultText, out var match)) continue;
                var matchKey = MatchKey(r.RoundName, match.Player1!, match.Player2!);
                if (!seenMatchKeys.Add(matchKey)) continue;
                if (!matchesByRound.TryGetValue(r.RoundName, out var list))
                {
                    list = new List<CacheMatch>();
                    matchesByRound[r.RoundName] = list;
                }
                list.Add(match);
            }
        }

        if (cacheDecks.Count == 0) return (null, "");

        var cacheRounds = matchesByRound
            .Select(kv => new CacheRound { RoundName = kv.Key, Matches = kv.Value.ToArray() })
            .ToArray();

        var item = new CacheItem
        {
            Tournament = new CacheTournament
            {
                Date = DateTime.SpecifyKind(search.StartDate, DateTimeKind.Utc),
                Name = NormalizeSpaces(search.Name ?? $"Melee tournament {search.ID}"),
                Uri = new Uri(string.Format(TournamentUrlTemplate, search.ID))
            },
            Decks = cacheDecks.ToArray(),
            Rounds = cacheRounds,
            Standings = cacheStandings.ToArray()
        };

        var hash = HashHelper.Sha256Hex(JsonSerializer.Serialize(item));
        return (item, hash);
    }

    private static bool TryBuildMatch(string playerName, string opponent, string resultText, out CacheMatch match)
    {
        match = new CacheMatch();
        if (string.IsNullOrWhiteSpace(resultText) || string.IsNullOrWhiteSpace(opponent)) return false;

        var trimmed = resultText.Trim();
        if (trimmed.StartsWith("Not reported", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.EndsWith("[FORMAT EXCEPTION]", StringComparison.OrdinalIgnoreCase)) return false;

        var score = ScoreRegex().Match(trimmed);
        string result;
        if (score.Success)
        {
            result = score.Groups[3].Success
                ? $"{score.Groups[1].Value}-{score.Groups[2].Value}-{score.Groups[3].Value}"
                : $"{score.Groups[1].Value}-{score.Groups[2].Value}-0";
        }
        else if (trimmed.Contains("bye", StringComparison.OrdinalIgnoreCase))
        {
            result = "2-0-0";
        }
        else
        {
            return false;
        }

        // Determinar orden Player1/Player2 según quién ganó.
        if (trimmed.StartsWith(playerName + " won", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("bye", StringComparison.OrdinalIgnoreCase))
        {
            match = new CacheMatch { Player1 = playerName, Player2 = opponent, Result = result };
        }
        else if (trimmed.StartsWith(opponent + " won", StringComparison.OrdinalIgnoreCase))
        {
            match = new CacheMatch { Player1 = opponent, Player2 = playerName, Result = result };
        }
        else if (trimmed.EndsWith("Draw", StringComparison.OrdinalIgnoreCase))
        {
            // Ordenar lexicográficamente para canonicalizar.
            var (p1, p2) = string.Compare(playerName, opponent, StringComparison.Ordinal) < 0
                ? (playerName, opponent) : (opponent, playerName);
            match = new CacheMatch { Player1 = p1, Player2 = p2, Result = result };
        }
        else
        {
            // No conseguimos determinar el winner — descartamos el match.
            return false;
        }
        return true;
    }

    private static string MatchKey(string roundName, string p1, string p2)
    {
        var ordered = string.Compare(p1, p2, StringComparison.Ordinal) < 0 ? $"{p1}|{p2}" : $"{p2}|{p1}";
        return $"{roundName}::{ordered}";
    }

    private static string NormalizeSpaces(string s) => Regex.Replace(s, @"\s+", " ").Trim();
}
