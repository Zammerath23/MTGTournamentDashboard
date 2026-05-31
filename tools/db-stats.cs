#:package Microsoft.Data.Sqlite@9.0.0

using Microsoft.Data.Sqlite;

var db = @"F:\AppData\MTGTournamentDashboard\db\meta.db";
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
        var vals = Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i)?.ToString() ?? "").ToArray();
        Console.WriteLine(string.Join(" | ", vals));
    }
    Console.WriteLine();
}

Q("Counts", @"SELECT 'tournaments' AS tbl, COUNT(*) AS n FROM Tournaments
              UNION ALL SELECT 'decks', COUNT(*) FROM Decks
              UNION ALL SELECT 'rounds', COUNT(*) FROM Rounds
              UNION ALL SELECT 'players', COUNT(*) FROM Players;");

Q("By source", "SELECT Source, COUNT(*) AS tournaments, SUM(PlayerCount) AS total_players FROM Tournaments GROUP BY Source;");

Q("Date range", "SELECT MIN(Date) AS oldest, MAX(Date) AS newest FROM Tournaments;");

Q("Status / IncludeInWinrate", "SELECT Status, IncludeInWinrate, COUNT(*) FROM Tournaments GROUP BY Status, IncludeInWinrate;");

Q("Top 10 most recent tournaments", "SELECT Date, Source, Name, PlayerCount FROM Tournaments ORDER BY Date DESC LIMIT 10;");

Q("Archetype coverage", @"SELECT
    SUM(CASE WHEN Archetype IS NULL THEN 1 ELSE 0 END) AS null_archetype,
    SUM(CASE WHEN Archetype = 'Unknown' THEN 1 ELSE 0 END) AS unknown_archetype,
    SUM(CASE WHEN Archetype IS NOT NULL AND Archetype <> 'Unknown' THEN 1 ELSE 0 END) AS classified,
    COUNT(*) AS total
  FROM Decks d
  INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern';");

Q("Top 20 archetypes", @"SELECT d.Archetype, COUNT(*) AS decks
  FROM Decks d
  INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY d.Archetype
  ORDER BY decks DESC
  LIMIT 20;");

Q("Rules version", "SELECT ArchetypeRulesVersion, COUNT(*) FROM Decks WHERE Archetype IS NOT NULL GROUP BY ArchetypeRulesVersion;");

Q("Winrate by source (Modern, leagues excluded)", @"
  SELECT t.Source,
         COUNT(*) AS decks,
         SUM(d.Wins) AS wins,
         SUM(d.Losses) AS losses,
         ROUND(CAST(SUM(d.Wins) AS REAL) / NULLIF(SUM(d.Wins) + SUM(d.Losses), 0), 3) AS winrate
  FROM Decks d INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source;");

Q("Avg deck record by source", @"
  SELECT t.Source,
         ROUND(AVG(d.Wins), 2) AS avg_wins,
         ROUND(AVG(d.Losses), 2) AS avg_losses,
         AVG(t.PlayerCount) AS avg_player_count,
         COUNT(DISTINCT t.Id) AS tournaments
  FROM Decks d INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source;");

Q("Record buckets per source (top 10)", @"
  SELECT t.Source, (d.Wins || '-' || d.Losses) AS record, COUNT(*) AS n
  FROM Decks d INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Format = 'Modern' AND t.IncludeInWinrate = 1
  GROUP BY t.Source, record
  ORDER BY t.Source, n DESC
  LIMIT 16;");

Q("Most recent tournament per source", @"
  SELECT t.Source, MAX(t.Date) AS newest, COUNT(*) AS n
  FROM Tournaments t WHERE t.Format = 'Modern'
  GROUP BY t.Source;");

Q("Recent Melee tournaments (top 15 by date)", @"
  SELECT Date, Name, PlayerCount FROM Tournaments
  WHERE Format = 'Modern' AND Source = 'Melee'
  ORDER BY Date DESC LIMIT 15;");

Q("Archetype coverage on Melee direct (most recent)", @"
  SELECT t.Date, t.Name,
         COUNT(*) AS decks,
         SUM(CASE WHEN d.Archetype IS NULL THEN 1 ELSE 0 END) AS null_arch,
         SUM(CASE WHEN d.Archetype = 'Unknown' THEN 1 ELSE 0 END) AS unknown,
         SUM(CASE WHEN d.Archetype IS NOT NULL AND d.Archetype <> 'Unknown' THEN 1 ELSE 0 END) AS classified,
         LENGTH(MIN(d.MainboardJson)) AS min_main_json_len
  FROM Decks d INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Source = 'Melee' AND t.Format = 'Modern'
  GROUP BY t.Id
  ORDER BY t.Date DESC LIMIT 10;");

Q("Sample mainboard JSON (newest Melee deck)", @"
  SELECT t.Name AS tournament, d.MainboardJson FROM Decks d
  INNER JOIN Tournaments t ON t.Id = d.TournamentId
  WHERE t.Source = 'Melee' AND t.Format = 'Modern'
  ORDER BY t.Date DESC, d.Id DESC LIMIT 1;");

Q("Tournaments completamente corruptos (TODOS los decks con MB vacio) — candidatos a borrado", @"
  SELECT t.Id, t.Date, t.Source, t.Name, COUNT(*) AS decks
  FROM Tournaments t INNER JOIN Decks d ON d.TournamentId = t.Id
  WHERE t.Source = 'Melee'
  GROUP BY t.Id
  HAVING SUM(CASE WHEN d.MainboardJson = '[]' THEN 0 ELSE 1 END) = 0
  ORDER BY t.Date DESC;");
