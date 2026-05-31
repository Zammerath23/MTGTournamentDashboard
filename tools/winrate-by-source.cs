#:package Microsoft.Data.Sqlite@9.0.0

using Microsoft.Data.Sqlite;

var db = @"F:\MTGTournamentDashboard\src\MTGTournamentDashboard\meta.db";
using var cn = new SqliteConnection($"Data Source={db}");
cn.Open();

void Q(string title, string sql)
{
    using var cmd = cn.CreateCommand();
    cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    Console.WriteLine($"== {title} ==");
    var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", cols));
    while (r.Read())
    {
        var vals = Enumerable.Range(0, r.FieldCount)
            .Select(i => r.IsDBNull(i) ? "" : Format(r.GetValue(i)))
            .ToArray();
        Console.WriteLine(string.Join(" | ", vals));
    }
    Console.WriteLine();
}

static string Format(object v) => v switch
{
    double d => d.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
    _ => v?.ToString() ?? ""
};

Q("Global winrate by source (Modern, leagues excluded)", @"
  SELECT t.Source,
         COUNT(*) AS decks,
         SUM(d.Wins) AS wins,
         SUM(d.Losses) AS losses,
         CAST(SUM(d.Wins) AS REAL) / NULLIF(SUM(d.Wins) + SUM(d.Losses), 0) AS winrate
  FROM Decks d
  INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source;");

Q("Decks per tournament (sample of MTGO vs Melee)", @"
  SELECT t.Source,
         COUNT(*) AS tournaments,
         AVG(t.PlayerCount) AS avg_player_count,
         MIN(t.PlayerCount) AS min_pc,
         MAX(t.PlayerCount) AS max_pc
  FROM Tournaments t
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source;");

Q("Avg wins per deck by source (top of meta only)", @"
  SELECT t.Source, AVG(d.Wins) AS avg_wins, AVG(d.Losses) AS avg_losses
  FROM Decks d INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source;");

Q("Distribution of deck records by source (W-L bucket)", @"
  SELECT t.Source,
         (d.Wins || '-' || d.Losses) AS record,
         COUNT(*) AS n
  FROM Decks d INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source, record
  ORDER BY t.Source, n DESC
  LIMIT 30;");
