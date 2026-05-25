namespace MTGTournamentDashboard.Data.Entities;

public class Round
{
    public int Id { get; set; }

    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;

    public int RoundNumber { get; set; }

    public int DeckAId { get; set; }
    public Deck DeckA { get; set; } = null!;

    public int DeckBId { get; set; }
    public Deck DeckB { get; set; } = null!;

    public int? WinnerDeckId { get; set; }
    public Deck? WinnerDeck { get; set; }

    public int GamesA { get; set; }
    public int GamesB { get; set; }
}
