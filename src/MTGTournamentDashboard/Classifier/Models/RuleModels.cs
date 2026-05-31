using System.Text.Json.Serialization;

namespace MTGTournamentDashboard.Classifier.Models;

public sealed class ArchetypeRule
{
    public string Name { get; set; } = "";
    public bool IncludeColorInName { get; set; }
    public ArchetypeCondition[]? Conditions { get; set; }
    public ArchetypeRule[]? Variants { get; set; }
}

public sealed class ArchetypeCondition
{
    public ConditionType Type { get; set; }
    public string[]? Cards { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConditionType
{
    Unknown = 0,
    InMainboard,
    InSideboard,
    OneOrMoreInMainboard,
    OneOrMoreInSideboard,
    TwoOrMoreInMainboard,
    TwoOrMoreInSideboard,
    DoesNotContain,
    DoesNotContainMainboard,
    DoesNotContainSideboard
}

public sealed class FallbackRule
{
    public string Name { get; set; } = "";
    public bool IncludeColorInName { get; set; }
    public string[]? CommonCards { get; set; }
}
