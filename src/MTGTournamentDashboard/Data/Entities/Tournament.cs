namespace MTGTournamentDashboard.Data.Entities;

public class Tournament
{
    public int Id { get; set; }

    public string Source { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public DateTime Date { get; set; }
    public int? PlayerCount { get; set; }

    public TournamentStatus Status { get; set; } = TournamentStatus.InProgress;
    public string SourceHash { get; set; } = "";
    public DateTime FetchedAt { get; set; }

    public bool IncludeInWinrate { get; set; } = true;

    public List<Deck> Decks { get; set; } = new();
    public List<Round> Rounds { get; set; } = new();
}

public enum TournamentStatus
{
    InProgress = 0,
    Completed = 1
}
