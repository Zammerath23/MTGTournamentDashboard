namespace MTGTournamentDashboard.Data.Entities;

public class Player
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public string? Handle { get; set; }

    public List<Deck> Decks { get; set; } = new();
}
