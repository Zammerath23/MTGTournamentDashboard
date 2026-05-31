using System.Text.RegularExpressions;

namespace MTGTournamentDashboard.Sync;

public static partial class ResultParser
{
    [GeneratedRegex(@"^\s*(\d+)\s*-\s*(\d+)(?:\s*-\s*(\d+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericResultRegex();

    public readonly record struct ParsedDeckRecord(int Wins, int Losses, int Draws, bool Parsed);

    public static ParsedDeckRecord ParseDeckResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return new(0, 0, 0, false);
        var match = NumericResultRegex().Match(result);
        if (!match.Success) return new(0, 0, 0, false);

        var wins = int.Parse(match.Groups[1].Value);
        var losses = int.Parse(match.Groups[2].Value);
        var draws = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return new(wins, losses, draws, true);
    }

    public readonly record struct ParsedMatchResult(int GamesP1, int GamesP2, int Draws, bool Parsed);

    public static ParsedMatchResult ParseMatchResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return new(0, 0, 0, false);
        var match = NumericResultRegex().Match(result);
        if (!match.Success) return new(0, 0, 0, false);

        var g1 = int.Parse(match.Groups[1].Value);
        var g2 = int.Parse(match.Groups[2].Value);
        var draws = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return new(g1, g2, draws, true);
    }

    [GeneratedRegex(@"(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRegex();

    public static int? ParseRoundNumber(string? roundName)
    {
        if (string.IsNullOrWhiteSpace(roundName)) return null;
        var match = DigitRegex().Match(roundName);
        if (!match.Success) return null;
        return int.Parse(match.Groups[1].Value);
    }
}
