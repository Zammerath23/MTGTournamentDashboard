using Microsoft.EntityFrameworkCore;
using MTGTournamentDashboard.Data;
using MTGTournamentDashboard.Data.Entities;

namespace MTGTournamentDashboard.Querying;

public sealed record ArchetypeStat(
    string Archetype,
    int DeckCount,
    int Wins,
    int Losses,
    int Draws,
    double MetaShare,
    double Winrate)
{
    public int MatchCount => Wins + Losses + Draws;
    public bool LowSample => MatchCount < 30;
}

public sealed record MetagameSnapshot(
    DateTime From,
    DateTime To,
    int TotalDecks,
    int TotalTournaments,
    IReadOnlyList<ArchetypeStat> Archetypes);

public sealed record ArchetypeDeckRow(
    int DeckId,
    string PlayerName,
    int TournamentId,
    string TournamentName,
    string TournamentSource,
    DateTime TournamentDate,
    int Wins,
    int Losses,
    int Draws,
    int? FinalRank,
    string MainboardJson,
    string SideboardJson);

public sealed record TournamentRow(
    int Id,
    DateTime Date,
    string Source,
    string Name,
    int? PlayerCount,
    int DeckCount,
    bool IncludeInWinrate,
    TournamentStatus Status,
    string SourceUrl);

/// <summary>A directional record: <c>Wins</c>/<c>Losses</c> from the ROW archetype's perspective.</summary>
public sealed record MatchupCell(int Wins, int Losses)
{
    public int Matches => Wins + Losses;
    public double? Winrate => Matches > 0 ? (double)Wins / Matches : null;
}

public sealed record MatchupMatrix(
    IReadOnlyList<string> Archetypes,
    IReadOnlyDictionary<(string Row, string Col), MatchupCell> Cells,
    int MinSample,
    int TotalRounds);

public static class TournamentSources
{
    public const string Melee = "Melee";
    public const string Mtgo = "Mtgo";
    public const string ManaTraders = "ManaTraders";

    public static readonly IReadOnlyList<string> All = new[] { Melee, Mtgo, ManaTraders };

    /// <summary>Sources whose published deck pool is fully representative (no top-cut bias).</summary>
    public static readonly IReadOnlyList<string> UnbiasedWinrate = new[] { Melee };
}

public sealed class MetagameQuery
{
    private readonly IDbContextFactory<MetaDbContext> _factory;

    public MetagameQuery(IDbContextFactory<MetaDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<(DateTime Min, DateTime Max)?> GetAvailableRangeAsync(string format, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Tournaments.AsNoTracking().Where(t => t.Format == format);
        if (!await q.AnyAsync(ct)) return null;
        var min = await q.MinAsync(t => t.Date, ct);
        var max = await q.MaxAsync(t => t.Date, ct);
        return (min, max);
    }

    public async Task<MetagameSnapshot> GetSnapshotAsync(string format, DateTime from, DateTime to, bool includeLeagues, IReadOnlyCollection<string>? sources = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var tournaments = db.Tournaments.AsNoTracking()
            .Where(t => t.Format == format)
            .Where(t => t.Date >= from && t.Date <= to);
        if (!includeLeagues) tournaments = tournaments.Where(t => t.IncludeInWinrate);
        if (sources is { Count: > 0 }) tournaments = tournaments.Where(t => sources.Contains(t.Source));

        var tournamentCount = await tournaments.CountAsync(ct);

        var decks = db.Decks.AsNoTracking()
            .Where(d => d.Archetype != null)
            .Where(d => d.Tournament.Format == format)
            .Where(d => d.Tournament.Date >= from && d.Tournament.Date <= to);
        if (!includeLeagues) decks = decks.Where(d => d.Tournament.IncludeInWinrate);
        if (sources is { Count: > 0 }) decks = decks.Where(d => sources.Contains(d.Tournament.Source));

        var grouped = await decks
            .GroupBy(d => d.Archetype!)
            .Select(g => new
            {
                Archetype = g.Key,
                DeckCount = g.Count(),
                Wins = g.Sum(d => d.Wins),
                Losses = g.Sum(d => d.Losses),
                Draws = g.Sum(d => d.Draws)
            })
            .ToListAsync(ct);

        var totalDecks = grouped.Sum(x => x.DeckCount);

        var stats = grouped.Select(g =>
            {
                var matches = g.Wins + g.Losses;
                var winrate = matches > 0 ? (double)g.Wins / matches : 0d;
                var share = totalDecks > 0 ? (double)g.DeckCount / totalDecks : 0d;
                return new ArchetypeStat(g.Archetype, g.DeckCount, g.Wins, g.Losses, g.Draws, share, winrate);
            })
            .OrderByDescending(s => s.DeckCount)
            .ToList();

        return new MetagameSnapshot(from, to, totalDecks, tournamentCount, stats);
    }

    public async Task<(ArchetypeStat? Header, IReadOnlyList<ArchetypeDeckRow> Decks)> GetArchetypeDetailAsync(
        string archetype, string format, DateTime from, DateTime to, bool includeLeagues,
        IReadOnlyCollection<string>? sources = null,
        int deckLimit = 100, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var decksQuery = db.Decks.AsNoTracking()
            .Where(d => d.Archetype == archetype)
            .Where(d => d.Tournament.Format == format)
            .Where(d => d.Tournament.Date >= from && d.Tournament.Date <= to);
        if (!includeLeagues) decksQuery = decksQuery.Where(d => d.Tournament.IncludeInWinrate);
        if (sources is { Count: > 0 }) decksQuery = decksQuery.Where(d => sources.Contains(d.Tournament.Source));

        var agg = await decksQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                DeckCount = g.Count(),
                Wins = g.Sum(d => d.Wins),
                Losses = g.Sum(d => d.Losses),
                Draws = g.Sum(d => d.Draws)
            })
            .FirstOrDefaultAsync(ct);

        // Need total decks in the same date+format+league+source filter to compute share.
        var totalScope = db.Decks.AsNoTracking()
            .Where(d => d.Archetype != null)
            .Where(d => d.Tournament.Format == format)
            .Where(d => d.Tournament.Date >= from && d.Tournament.Date <= to);
        if (!includeLeagues) totalScope = totalScope.Where(d => d.Tournament.IncludeInWinrate);
        if (sources is { Count: > 0 }) totalScope = totalScope.Where(d => sources.Contains(d.Tournament.Source));
        var total = await totalScope.CountAsync(ct);

        ArchetypeStat? header = null;
        if (agg is not null)
        {
            var matches = agg.Wins + agg.Losses;
            var winrate = matches > 0 ? (double)agg.Wins / matches : 0d;
            var share = total > 0 ? (double)agg.DeckCount / total : 0d;
            header = new ArchetypeStat(archetype, agg.DeckCount, agg.Wins, agg.Losses, agg.Draws, share, winrate);
        }

        var rows = await decksQuery
            .OrderByDescending(d => d.Wins)
            .ThenBy(d => d.Losses)
            .ThenBy(d => d.FinalRank ?? int.MaxValue)
            .ThenByDescending(d => d.Tournament.Date)
            .Take(deckLimit)
            .Select(d => new ArchetypeDeckRow(
                d.Id,
                d.Player.Name,
                d.Tournament.Id,
                d.Tournament.Name,
                d.Tournament.Source,
                d.Tournament.Date,
                d.Wins,
                d.Losses,
                d.Draws,
                d.FinalRank,
                d.MainboardJson,
                d.SideboardJson))
            .ToListAsync(ct);

        return (header, rows);
    }

    public async Task<IReadOnlyList<TournamentRow>> GetTournamentsAsync(
        string format, DateTime from, DateTime to,
        IReadOnlyCollection<string>? sources = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Tournaments.AsNoTracking()
            .Where(t => t.Format == format)
            .Where(t => t.Date >= from && t.Date <= to);
        if (sources is { Count: > 0 }) q = q.Where(t => sources.Contains(t.Source));

        return await q
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Name)
            .Select(t => new TournamentRow(
                t.Id,
                t.Date,
                t.Source,
                t.Name,
                t.PlayerCount,
                t.Decks.Count,
                t.IncludeInWinrate,
                t.Status,
                t.SourceUrl))
            .ToListAsync(ct);
    }

    /// <summary>
    /// NxN directional winrate matrix between the top archetypes in scope. Cell (Row, Col) holds the
    /// Row archetype's W-L vs the Col archetype. Draws are excluded from the denominator; the mirror
    /// (Row == Col) is omitted. Built from decided <see cref="Round"/>s only.
    /// </summary>
    public async Task<MatchupMatrix> GetMatchupMatrixAsync(
        string format, DateTime from, DateTime to, bool includeLeagues,
        IReadOnlyCollection<string>? sources = null,
        int topN = 12, int minSample = 20, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // 1. Top-N archetypes by deck count in scope (ignora null y "Unknown").
        var deckScope = db.Decks.AsNoTracking()
            .Where(d => d.Archetype != null && d.Archetype != "Unknown")
            .Where(d => d.Tournament.Format == format)
            .Where(d => d.Tournament.Date >= from && d.Tournament.Date <= to);
        if (!includeLeagues) deckScope = deckScope.Where(d => d.Tournament.IncludeInWinrate);
        if (sources is { Count: > 0 }) deckScope = deckScope.Where(d => sources.Contains(d.Tournament.Source));

        var topArchetypes = await deckScope
            .GroupBy(d => d.Archetype!)
            .Select(g => new { Archetype = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topN)
            .Select(x => x.Archetype)
            .ToListAsync(ct);

        var topSet = topArchetypes.ToHashSet(StringComparer.Ordinal);
        if (topSet.Count == 0)
            return new MatchupMatrix(topArchetypes, new Dictionary<(string, string), MatchupCell>(), minSample, 0);

        // 2. Rondas decididas, ambos arquetipos en el top-N, dentro del filtro.
        var roundsQuery = db.Rounds.AsNoTracking()
            .Where(r => r.WinnerDeckId != null)
            .Where(r => r.DeckA.Archetype != null && r.DeckB.Archetype != null)
            .Where(r => topSet.Contains(r.DeckA.Archetype!) && topSet.Contains(r.DeckB.Archetype!))
            .Where(r => r.Tournament.Format == format)
            .Where(r => r.Tournament.Date >= from && r.Tournament.Date <= to);
        if (!includeLeagues) roundsQuery = roundsQuery.Where(r => r.Tournament.IncludeInWinrate);
        if (sources is { Count: > 0 }) roundsQuery = roundsQuery.Where(r => sources.Contains(r.Tournament.Source));

        var rounds = await roundsQuery
            .Select(r => new
            {
                A = r.DeckA.Archetype!,
                B = r.DeckB.Archetype!,
                AWon = r.WinnerDeckId == r.DeckAId
            })
            .ToListAsync(ct);

        // 3. Agregación direccional en memoria (cada ronda alimenta las dos celdas espejo).
        var wins = new Dictionary<(string, string), int>();
        var losses = new Dictionary<(string, string), int>();
        static void Bump(Dictionary<(string, string), int> map, string row, string col)
            => map[(row, col)] = map.TryGetValue((row, col), out var v) ? v + 1 : 1;

        foreach (var r in rounds)
        {
            if (r.A == r.B) continue; // mirror: no aporta a la matriz
            var winner = r.AWon ? r.A : r.B;
            var loser = r.AWon ? r.B : r.A;
            Bump(wins, winner, loser);
            Bump(losses, loser, winner);
        }

        var cells = new Dictionary<(string Row, string Col), MatchupCell>();
        foreach (var key in wins.Keys.Concat(losses.Keys).Distinct())
        {
            var w = wins.TryGetValue(key, out var wv) ? wv : 0;
            var l = losses.TryGetValue(key, out var lv) ? lv : 0;
            cells[key] = new MatchupCell(w, l);
        }

        return new MatchupMatrix(topArchetypes, cells, minSample, rounds.Count);
    }
}
