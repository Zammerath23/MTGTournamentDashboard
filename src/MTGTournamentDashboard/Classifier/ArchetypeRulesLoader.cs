using System.Text.Json;
using MTGTournamentDashboard.Classifier.Models;

namespace MTGTournamentDashboard.Classifier;

public sealed class ArchetypeRulesLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public FormatRules Load(string repoRoot, string format, string rulesVersion)
    {
        var formatRoot = Path.Combine(repoRoot, "Formats", format);
        if (!Directory.Exists(formatRoot))
            throw new DirectoryNotFoundException($"Format folder not found: {formatRoot}");

        var archetypes = LoadAll<ArchetypeRule>(Path.Combine(formatRoot, "Archetypes"));
        var fallbacks = LoadAll<FallbackRule>(Path.Combine(formatRoot, "Fallbacks"));

        return new FormatRules
        {
            Format = format,
            RulesVersion = rulesVersion,
            Archetypes = archetypes,
            Fallbacks = fallbacks
        };
    }

    private static IReadOnlyList<T> LoadAll<T>(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<T>();
        var result = new List<T>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var s = File.OpenRead(file);
                var item = JsonSerializer.Deserialize<T>(s, JsonOpts);
                if (item is not null) result.Add(item);
            }
            catch (JsonException)
            {
                // Skip malformed rule files rather than aborting the whole classifier.
            }
        }
        return result;
    }
}
