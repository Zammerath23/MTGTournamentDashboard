namespace MTGTournamentDashboard.Sync.Models;

public sealed class CacheItem
{
    public CacheTournament? Tournament { get; set; }
    public CacheDeck[]? Decks { get; set; }
    public CacheRound[]? Rounds { get; set; }
    public CacheStanding[]? Standings { get; set; }
}

public sealed class CacheTournament
{
    public DateTime Date { get; set; }
    public string? Name { get; set; }
    public Uri? Uri { get; set; }
    public string? JsonFile { get; set; }
    public bool ForceRedownload { get; set; }
    public string[]? ExcludedRounds { get; set; }
}

public sealed class CacheDeck
{
    public DateTime? Date { get; set; }
    public string? Player { get; set; }
    public string? Result { get; set; }
    public Uri? AnchorUri { get; set; }
    public CacheDeckItem[]? Mainboard { get; set; }
    public CacheDeckItem[]? Sideboard { get; set; }
}

public sealed class CacheDeckItem
{
    public int Count { get; set; }
    public string? CardName { get; set; }
}

public sealed class CacheRound
{
    public string? RoundName { get; set; }
    public CacheMatch[]? Matches { get; set; }
}

public sealed class CacheMatch
{
    public string? Player1 { get; set; }
    public string? Player2 { get; set; }
    public string? Result { get; set; }
}

public sealed class CacheStanding
{
    public int Rank { get; set; }
    public string? Player { get; set; }
    public int Points { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public double OMWP { get; set; }
    public double GWP { get; set; }
    public double OGWP { get; set; }
}
