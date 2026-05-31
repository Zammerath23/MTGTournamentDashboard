using MTGTournamentDashboard.Classifier.Models;

namespace MTGTournamentDashboard.Classifier;

public sealed class ArchetypeClassifier
{
    private readonly FormatRules _rules;

    public ArchetypeClassifier(FormatRules rules)
    {
        _rules = rules;
    }

    public string RulesVersion => _rules.RulesVersion;

    public string? Classify(IReadOnlyDictionary<string, int> mainboard, IReadOnlyDictionary<string, int> sideboard)
    {
        foreach (var arch in _rules.Archetypes)
        {
            if (!Matches(arch.Conditions, mainboard, sideboard)) continue;

            // First matching variant wins; otherwise base archetype name.
            if (arch.Variants is { Length: > 0 })
            {
                foreach (var variant in arch.Variants)
                {
                    if (Matches(variant.Conditions, mainboard, sideboard))
                        return variant.Name;
                }
            }
            return arch.Name;
        }

        // Fallback: pick the rule with most CommonCards present.
        FallbackRule? best = null;
        int bestScore = 0;
        foreach (var fb in _rules.Fallbacks)
        {
            if (fb.CommonCards is null) continue;
            int score = 0;
            foreach (var card in fb.CommonCards)
            {
                if (mainboard.ContainsKey(card) || sideboard.ContainsKey(card)) score++;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = fb;
            }
        }

        return best is null || bestScore == 0 ? "Unknown" : best.Name;
    }

    private static bool Matches(ArchetypeCondition[]? conditions,
                                IReadOnlyDictionary<string, int> mb,
                                IReadOnlyDictionary<string, int> sb)
    {
        if (conditions is null || conditions.Length == 0) return true;
        foreach (var c in conditions)
        {
            if (!EvaluateCondition(c, mb, sb)) return false;
        }
        return true;
    }

    private static bool EvaluateCondition(ArchetypeCondition c,
                                          IReadOnlyDictionary<string, int> mb,
                                          IReadOnlyDictionary<string, int> sb)
    {
        var cards = c.Cards ?? Array.Empty<string>();
        return c.Type switch
        {
            ConditionType.InMainboard            => cards.All(mb.ContainsKey),
            ConditionType.InSideboard            => cards.All(sb.ContainsKey),
            ConditionType.OneOrMoreInMainboard   => cards.Any(mb.ContainsKey),
            ConditionType.OneOrMoreInSideboard   => cards.Any(sb.ContainsKey),
            ConditionType.TwoOrMoreInMainboard   => cards.Count(mb.ContainsKey) >= 2,
            ConditionType.TwoOrMoreInSideboard   => cards.Count(sb.ContainsKey) >= 2,
            ConditionType.DoesNotContain         => cards.All(k => !mb.ContainsKey(k) && !sb.ContainsKey(k)),
            ConditionType.DoesNotContainMainboard => cards.All(k => !mb.ContainsKey(k)),
            ConditionType.DoesNotContainSideboard => cards.All(k => !sb.ContainsKey(k)),
            _ => true
        };
    }
}
