namespace MTGTournamentDashboard.Sync.Melee;

// Minimal DTOs para deserializar las respuestas JSON de Melee.gg.
// Solo extraigo los campos que necesito; ignoro el resto.

internal sealed class TournamentSearchResponse
{
    public int recordsTotal { get; set; }
    public TournamentSearchItem[]? data { get; set; }
}

internal sealed class TournamentSearchItem
{
    public int ID { get; set; }
    public DateTime StartDate { get; set; }
    public int Decklists { get; set; }
    public string? Name { get; set; }
    public string? OrganizationName { get; set; }
    public string? FormatDescription { get; set; }
    public string? StatusDescription { get; set; }
}

internal sealed class StandingsResponse
{
    public StandingsItem[]? data { get; set; }
}

internal sealed class StandingsItem
{
    public int Rank { get; set; }
    public int Points { get; set; }
    public double OpponentMatchWinPercentage { get; set; }
    public double TeamGameWinPercentage { get; set; }
    public double OpponentGameWinPercentage { get; set; }
    public int MatchWins { get; set; }
    public int MatchLosses { get; set; }
    public int MatchDraws { get; set; }
    public StandingsTeam? Team { get; set; }
    public StandingsDecklist[]? Decklists { get; set; }
}

internal sealed class StandingsTeam
{
    public StandingsPlayer[]? Players { get; set; }
}

internal sealed class StandingsPlayer
{
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
}

internal sealed class StandingsDecklist
{
    public string? DecklistId { get; set; }
    public string? Format { get; set; }
}
