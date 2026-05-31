using MTGTournamentDashboard.Classifier.Models;

namespace MTGTournamentDashboard.Classifier;

public sealed class FormatRules
{
    public required string Format { get; init; }
    public required string RulesVersion { get; init; }
    public required IReadOnlyList<ArchetypeRule> Archetypes { get; init; }
    public required IReadOnlyList<FallbackRule> Fallbacks { get; init; }
}
