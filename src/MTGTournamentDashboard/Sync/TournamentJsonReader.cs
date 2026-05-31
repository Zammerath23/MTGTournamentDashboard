using System.Text.Json;
using Microsoft.Extensions.Options;
using MTGTournamentDashboard.Sync.Models;

namespace MTGTournamentDashboard.Sync;

public sealed class TournamentJsonReader
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    // MTGO data is split across two folders in MTGODecklistCache: the legacy "mtgo.com" folder
    // (pre-2024-06-20 site) and "mtgo.com_limited_data" (post-redesign). Both belong to the
    // same logical source from our point of view.
    private static readonly Dictionary<string, string[]> SourceFolderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mtgo"] = new[] { "mtgo.com", "mtgo.com_limited_data" },
        ["Melee"] = new[] { "melee.gg" },
        ["ManaTraders"] = new[] { "manatraders.com" }
    };

    private readonly SyncOptions _options;

    public TournamentJsonReader(IOptions<SyncOptions> options)
    {
        _options = options.Value;
    }

    public IEnumerable<TournamentFile> EnumerateTournaments(string repoRoot, DateTime fromDateUtc)
    {
        var tournamentsRoot = Path.Combine(repoRoot, "Tournaments");
        if (!Directory.Exists(tournamentsRoot)) yield break;

        var enabledSources = new List<string>();
        if (_options.Sources.Mtgo) enabledSources.Add("Mtgo");
        if (_options.Sources.Melee) enabledSources.Add("Melee");
        if (_options.Sources.ManaTraders) enabledSources.Add("ManaTraders");

        var formatTokens = _options.Formats
            .Select(f => f.ToLowerInvariant())
            .ToArray();

        foreach (var sourceKey in enabledSources)
        foreach (var sourceFolder in SourceFolderMap[sourceKey])
        {
            var sourceRoot = Path.Combine(tournamentsRoot, sourceFolder);
            if (!Directory.Exists(sourceRoot)) continue;

            foreach (var yearDir in EnumerateDateDirs(sourceRoot, fromDateUtc.Year))
            {
                if (!int.TryParse(Path.GetFileName(yearDir), out var year)) continue;
                if (year < fromDateUtc.Year) continue;

                foreach (var monthDir in EnumerateDateDirs(yearDir, 1))
                {
                    if (!int.TryParse(Path.GetFileName(monthDir), out var month)) continue;
                    if (year == fromDateUtc.Year && month < fromDateUtc.Month) continue;

                    foreach (var dayDir in EnumerateDateDirs(monthDir, 1))
                    {
                        if (!int.TryParse(Path.GetFileName(dayDir), out var day)) continue;
                        DateTime folderDate;
                        try { folderDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc); }
                        catch { continue; }
                        if (folderDate < fromDateUtc.Date) continue;

                        foreach (var file in Directory.EnumerateFiles(dayDir, "*.json"))
                        {
                            var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                            if (!formatTokens.Any(tok => fileName.StartsWith(tok + "-") || fileName.Contains("-" + tok + "-"))) continue;

                            yield return new TournamentFile(
                                SourceKey: sourceKey,
                                SourceFolder: sourceFolder,
                                FilePath: file,
                                RelativePath: Path.GetRelativePath(repoRoot, file));
                        }
                    }
                }
            }
        }
    }

    public CacheItem? Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return JsonSerializer.Deserialize<CacheItem>(stream, JsonOpts);
    }

    public async Task<(CacheItem? Item, string Hash, string RawJson)> ReadWithHashAsync(string filePath, CancellationToken ct)
    {
        var raw = await File.ReadAllTextAsync(filePath, ct);
        var hash = HashHelper.Sha256Hex(raw);
        var item = JsonSerializer.Deserialize<CacheItem>(raw, JsonOpts);
        return (item, hash, raw);
    }

    private static IEnumerable<string> EnumerateDateDirs(string parent, int minValue)
    {
        return Directory.EnumerateDirectories(parent)
            .OrderBy(d => d, StringComparer.Ordinal);
    }
}

public sealed record TournamentFile(string SourceKey, string SourceFolder, string FilePath, string RelativePath);
