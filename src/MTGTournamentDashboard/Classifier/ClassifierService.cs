using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTGTournamentDashboard.Data;
using MTGTournamentDashboard.Sync;
using MTGTournamentDashboard.Sync.Models;

namespace MTGTournamentDashboard.Classifier;

public sealed class ClassifierService
{
    private readonly IDbContextFactory<MetaDbContext> _dbFactory;
    private readonly MtgoFormatDataClient _rulesClient;
    private readonly ArchetypeRulesLoader _loader;
    private readonly SyncProgress _progress;
    private readonly SyncOptions _options;
    private readonly ILogger<ClassifierService> _logger;

    public ClassifierService(
        IDbContextFactory<MetaDbContext> dbFactory,
        MtgoFormatDataClient rulesClient,
        ArchetypeRulesLoader loader,
        SyncProgress progress,
        IOptions<SyncOptions> options,
        ILogger<ClassifierService> logger)
    {
        _dbFactory = dbFactory;
        _rulesClient = rulesClient;
        _loader = loader;
        _progress = progress;
        _options = options.Value;
        _logger = logger;
    }

    /// <param name="reclassifyAll">
    /// false → only decks with Archetype IS NULL (incremental, runs after sync).
    /// true  → every deck (forces a full reclassify against the current rules version).
    /// </param>
    public async Task RunAsync(bool reclassifyAll, CancellationToken ct)
    {
        _progress.SetStep(reclassifyAll ? "Reclasificando todos los decks" : "Clasificando decks nuevos");

        var rulesSha = _rulesClient.EnsureUpToDate(_progress.Info, ct);

        var format = _options.Formats.FirstOrDefault() ?? "Modern";
        var rules = _loader.Load(_rulesClient.LocalPath, format, rulesSha);
        _progress.Info($"Reglas cargadas: {rules.Archetypes.Count} arquetipos, {rules.Fallbacks.Count} fallbacks ({format} @ {rulesSha})");

        var classifier = new ArchetypeClassifier(rules);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        int classified = 0, unchanged = 0, processed = 0, errors = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Stream decks by tournament format so we only touch Modern decks. The Tournament.Format
        // filter avoids reading every Deck row when there's other-format data in the DB later.
        var deckQuery = db.Decks.AsNoTracking()
            .Where(d => d.Tournament.Format == format);

        if (!reclassifyAll)
        {
            deckQuery = deckQuery.Where(d => d.Archetype == null);
        }

        var projected = deckQuery.Select(d => new DeckClassificationView
        {
            Id = d.Id,
            CurrentArchetype = d.Archetype,
            CurrentRulesVersion = d.ArchetypeRulesVersion,
            MainboardJson = d.MainboardJson,
            SideboardJson = d.SideboardJson
        });

        var pending = new List<(int Id, string Archetype, string RulesVersion)>(capacity: 500);

        await foreach (var d in projected.AsAsyncEnumerable().WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            try
            {
                var mb = ParseCounts(d.MainboardJson);
                var sb = ParseCounts(d.SideboardJson);
                var arch = classifier.Classify(mb, sb) ?? "Unknown";
                counts[arch] = counts.GetValueOrDefault(arch) + 1;

                if (d.CurrentArchetype == arch && d.CurrentRulesVersion == classifier.RulesVersion)
                {
                    unchanged++;
                    continue;
                }

                pending.Add((d.Id, arch, classifier.RulesVersion));
                if (pending.Count >= 500)
                {
                    await FlushAsync(pending, ct);
                    classified += pending.Count;
                    pending.Clear();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Classifier error on deck {Id}", d.Id);
            }

            if (processed % 2000 == 0)
            {
                _progress.Info($"Clasificador: {processed} procesados, {classified + pending.Count} actualizados");
            }
        }

        if (pending.Count > 0)
        {
            await FlushAsync(pending, ct);
            classified += pending.Count;
            pending.Clear();
        }

        _progress.Info($"Clasificador: {processed} decks, {classified} actualizados, {unchanged} sin cambio, {errors} err");

        var top = counts.OrderByDescending(kv => kv.Value).Take(8).ToList();
        if (top.Count > 0)
        {
            _progress.Info("Top arquetipos: " + string.Join(", ", top.Select(kv => $"{kv.Key} ({kv.Value})")));
        }
    }

    private async Task FlushAsync(List<(int Id, string Archetype, string RulesVersion)> pending, CancellationToken ct)
    {
        if (pending.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var (id, arch, ver) in pending)
        {
            await db.Decks
                .Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Archetype, arch)
                    .SetProperty(d => d.ArchetypeRulesVersion, ver), ct);
        }
        await tx.CommitAsync(ct);
    }

    private static Dictionary<string, int> ParseCounts(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new(StringComparer.OrdinalIgnoreCase);

        var items = JsonSerializer.Deserialize<CacheDeckItem[]>(json) ?? Array.Empty<CacheDeckItem>();
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.CardName)) continue;
            dict[item.CardName] = dict.GetValueOrDefault(item.CardName) + item.Count;
        }
        return dict;
    }

    private sealed class DeckClassificationView
    {
        public int Id { get; set; }
        public string? CurrentArchetype { get; set; }
        public string? CurrentRulesVersion { get; set; }
        public string MainboardJson { get; set; } = "[]";
        public string SideboardJson { get; set; } = "[]";
    }
}
