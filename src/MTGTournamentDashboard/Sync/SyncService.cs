using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTGTournamentDashboard.Classifier;
using MTGTournamentDashboard.Data;
using MTGTournamentDashboard.Data.Entities;
using MTGTournamentDashboard.Sync.Models;

namespace MTGTournamentDashboard.Sync;

public sealed class SyncService
{
    private readonly IDbContextFactory<MetaDbContext> _dbFactory;
    private readonly MtgoDecklistCacheClient _cacheClient;
    private readonly TournamentJsonReader _reader;
    private readonly ClassifierService _classifier;
    private readonly SyncProgress _progress;
    private readonly SyncOptions _options;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        IDbContextFactory<MetaDbContext> dbFactory,
        MtgoDecklistCacheClient cacheClient,
        TournamentJsonReader reader,
        ClassifierService classifier,
        SyncProgress progress,
        IOptions<SyncOptions> options,
        ILogger<SyncService> logger)
    {
        _dbFactory = dbFactory;
        _cacheClient = cacheClient;
        _reader = reader;
        _classifier = classifier;
        _progress = progress;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var fromDate = DateTime.UtcNow.Date.AddMonths(-_options.InitialHistoryMonths);
        _progress.BeginRun($"Iniciando sync (desde {fromDate:yyyy-MM-dd})");

        try
        {
            _cacheClient.EnsureUpToDate(_progress, ct);
            ct.ThrowIfCancellationRequested();

            _progress.SetStep("Enumerando ficheros de torneo");
            var files = _reader.EnumerateTournaments(_cacheClient.LocalPath, fromDate).ToList();
            _progress.Info($"Ficheros candidatos: {files.Count}");

            int processed = 0, inserted = 0, updated = 0, skipped = 0, failed = 0;
            var seenThisRun = new HashSet<(string Source, string Url)>();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                try
                {
                    var (item, hash, _) = await _reader.ReadWithHashAsync(file.FilePath, ct);
                    if (item?.Tournament?.Uri is null)
                    {
                        skipped++;
                        continue;
                    }

                    var key = (file.SourceKey, item.Tournament.Uri.ToString());
                    if (!seenThisRun.Add(key))
                    {
                        // Same logical tournament already processed via another folder (e.g. legacy
                        // mtgo.com vs mtgo.com_limited_data overlap). Skip silently to keep idempotency stable.
                        skipped++;
                        continue;
                    }

                    var outcome = await ProcessTournamentAsync(file, item, hash, ct);
                    switch (outcome)
                    {
                        case ProcessOutcome.Inserted: inserted++; break;
                        case ProcessOutcome.Updated: updated++; break;
                        case ProcessOutcome.Skipped: skipped++; break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Sync error on {File}", file.RelativePath);
                    _progress.Warn($"Error en {file.RelativePath}: {ex.Message}");
                }

                if (processed % 50 == 0)
                {
                    _progress.Info($"Progreso: {processed}/{files.Count} (ins {inserted}, upd {updated}, skip {skipped}, err {failed})");
                }
            }

            _progress.Info($"Resumen: ins {inserted}, upd {updated}, skip {skipped}, err {failed} sobre {files.Count}");

            await _classifier.RunAsync(reclassifyAll: false, ct);

            _progress.EndRun(success: true);
        }
        catch (OperationCanceledException)
        {
            _progress.EndRun(success: false, error: "Cancelado");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            _progress.EndRun(success: false, error: ex.Message);
            throw;
        }
    }

    private enum ProcessOutcome { Inserted, Updated, Skipped }

    /// <summary>
    /// Punto de entrada para fuentes externas al pipeline del clone (ej. Melee directo). Reutiliza
    /// la misma idempotencia + persistencia que el sync normal, sólo varía la procedencia del item.
    /// </summary>
    public Task ProcessExternalTournamentAsync(string sourceKey, string relativeIdentifier, CacheItem item, string hash, CancellationToken ct)
    {
        var stub = new TournamentFile(
            SourceKey: sourceKey,
            SourceFolder: sourceKey,
            FilePath: "",
            RelativePath: relativeIdentifier);
        return ProcessTournamentAsync(stub, item, hash, ct);
    }

    private async Task<ProcessOutcome> ProcessTournamentAsync(TournamentFile file, CacheItem item, string hash, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sourceUrl = item.Tournament!.Uri!.ToString();
        var existing = await db.Tournaments
            .Where(t => t.Source == file.SourceKey && t.SourceUrl == sourceUrl)
            .Select(t => new { t.Id, t.Status, t.SourceHash })
            .FirstOrDefaultAsync(ct);

        if (existing is { Status: TournamentStatus.Completed } && existing.SourceHash == hash)
        {
            return ProcessOutcome.Skipped;
        }

        var isUpdate = existing is not null;
        if (existing is not null)
        {
            _progress.Warn($"UPD {file.RelativePath}  db={existing.SourceHash[..Math.Min(8, existing.SourceHash.Length)]}  file={hash[..8]}  status={existing.Status}");
            await db.Tournaments
                .Where(t => t.Id == existing.Id)
                .ExecuteDeleteAsync(ct);
        }

        var tournament = MapTournament(file, item, hash);
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync(ct);

        var decksByPlayerKey = await MapDecksAsync(db, tournament, item, ct);
        await db.SaveChangesAsync(ct);

        if (file.SourceKey == "Melee" && item.Rounds is { Length: > 0 })
        {
            MapRounds(tournament, item, decksByPlayerKey);
            await db.SaveChangesAsync(ct);
        }

        tournament.Status = TournamentStatus.Completed;
        await db.SaveChangesAsync(ct);

        return isUpdate ? ProcessOutcome.Updated : ProcessOutcome.Inserted;
    }

    private Tournament MapTournament(TournamentFile file, CacheItem item, string hash)
    {
        var t = item.Tournament!;
        var name = t.Name ?? Path.GetFileNameWithoutExtension(file.FilePath);

        var format = ResolveFormat(file.FilePath) ?? _options.Formats.FirstOrDefault() ?? "Unknown";
        var isLeague = name.Contains("league", StringComparison.OrdinalIgnoreCase);

        var playerCount = item.Standings?.Length ?? item.Decks?.Length ?? 0;

        return new Tournament
        {
            Source = file.SourceKey,
            SourceUrl = t.Uri!.ToString(),
            Name = name,
            Format = format,
            Date = DateTime.SpecifyKind(t.Date, DateTimeKind.Utc),
            PlayerCount = playerCount > 0 ? playerCount : null,
            Status = TournamentStatus.InProgress,
            SourceHash = hash,
            FetchedAt = DateTime.UtcNow,
            IncludeInWinrate = !isLeague
        };
    }

    private async Task<Dictionary<string, Deck>> MapDecksAsync(MetaDbContext db, Tournament tournament, CacheItem item, CancellationToken ct)
    {
        var decks = new Dictionary<string, Deck>(StringComparer.OrdinalIgnoreCase);
        if (item.Decks is null) return decks;

        var standingsByPlayer = (item.Standings ?? Array.Empty<CacheStanding>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Player))
            .GroupBy(s => s.Player!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var d in item.Decks)
        {
            if (string.IsNullOrWhiteSpace(d.Player)) continue;
            var playerName = d.Player.Trim();

            var player = await GetOrCreatePlayerAsync(db, playerName, ct);

            var parsed = ResultParser.ParseDeckResult(d.Result);
            int wins = parsed.Wins, losses = parsed.Losses, draws = parsed.Draws;
            int? rank = null;

            if (standingsByPlayer.TryGetValue(playerName, out var standing))
            {
                if (!parsed.Parsed)
                {
                    wins = standing.Wins;
                    losses = standing.Losses;
                    draws = standing.Draws;
                }
                rank = standing.Rank;
            }

            var deck = new Deck
            {
                TournamentId = tournament.Id,
                Tournament = tournament,
                PlayerId = player.Id,
                Player = player,
                Archetype = null,
                ArchetypeRulesVersion = null,
                Wins = wins,
                Losses = losses,
                Draws = draws,
                FinalRank = rank,
                MainboardJson = JsonSerializer.Serialize(d.Mainboard ?? Array.Empty<CacheDeckItem>()),
                SideboardJson = JsonSerializer.Serialize(d.Sideboard ?? Array.Empty<CacheDeckItem>())
            };
            db.Decks.Add(deck);
            decks[playerName] = deck;
        }

        return decks;
    }

    private static void MapRounds(Tournament tournament, CacheItem item, Dictionary<string, Deck> decksByPlayer)
    {
        if (item.Rounds is null) return;

        int roundIndex = 0;
        foreach (var r in item.Rounds)
        {
            roundIndex++;
            var roundNumber = ResultParser.ParseRoundNumber(r.RoundName) ?? roundIndex;
            if (r.Matches is null) continue;

            foreach (var m in r.Matches)
            {
                if (string.IsNullOrWhiteSpace(m.Player1) || string.IsNullOrWhiteSpace(m.Player2)) continue;
                if (!decksByPlayer.TryGetValue(m.Player1.Trim(), out var deckA)) continue;
                if (!decksByPlayer.TryGetValue(m.Player2.Trim(), out var deckB)) continue;

                var games = ResultParser.ParseMatchResult(m.Result);
                if (!games.Parsed) continue;

                int? winnerId = null;
                if (games.GamesP1 > games.GamesP2) winnerId = deckA.Id;
                else if (games.GamesP2 > games.GamesP1) winnerId = deckB.Id;

                tournament.Rounds.Add(new Round
                {
                    TournamentId = tournament.Id,
                    Tournament = tournament,
                    RoundNumber = roundNumber,
                    DeckAId = deckA.Id,
                    DeckA = deckA,
                    DeckBId = deckB.Id,
                    DeckB = deckB,
                    WinnerDeckId = winnerId,
                    GamesA = games.GamesP1,
                    GamesB = games.GamesP2
                });
            }
        }
    }

    private static async Task<Player> GetOrCreatePlayerAsync(MetaDbContext db, string name, CancellationToken ct)
    {
        var existing = await db.Players.FirstOrDefaultAsync(p => p.Name == name && p.Handle == null, ct);
        if (existing is not null) return existing;

        var player = new Player { Name = name, Handle = null };
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);
        return player;
    }

    private string? ResolveFormat(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        foreach (var format in _options.Formats)
        {
            var token = format.ToLowerInvariant();
            if (fileName.StartsWith(token + "-") || fileName.Contains("-" + token + "-"))
                return format;
        }
        return null;
    }
}
