namespace MTGTournamentDashboard.Data.Entities;

public class Deck
{
    public int Id { get; set; }

    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;

    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public string? Archetype { get; set; }
    public string? ArchetypeRulesVersion { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }

    public int? FinalRank { get; set; }

    public string MainboardJson { get; set; } = "[]";
    public string SideboardJson { get; set; } = "[]";
}
