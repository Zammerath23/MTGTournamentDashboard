using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MTGTournamentDashboard.Sync.Melee;

/// <summary>
/// Wrapper sobre los 4 endpoints públicos de melee.gg que se usan para reconstruir
/// torneos completos (search + tournament page + standings + deck).
/// </summary>
internal sealed class MeleeApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<MeleeApiClient> _logger;

    public MeleeApiClient(HttpClient http, ILogger<MeleeApiClient> logger)
    {
        _http = http;
        _logger = logger;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(MeleeConstants.UserAgent);
        }
    }

    /// <summary>Lista todos los torneos en el rango [start, end]. Pagina automáticamente.</summary>
    public async Task<List<TournamentSearchItem>> ListTournamentsAsync(DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        var all = new List<TournamentSearchItem>();
        int offset = 0;
        int total = -1;

        while (total < 0 || offset < total)
        {
            ct.ThrowIfCancellationRequested();

            var body = MeleeConstants.TournamentListParameters
                .Replace("{offset}", offset.ToString())
                .Replace("{startDate}", startDate.ToString("yyyy-MM-dd"))
                .Replace("{endDate}", endDate.ToString("yyyy-MM-dd"));

            using var req = new HttpRequestMessage(HttpMethod.Post, MeleeConstants.TournamentListPage)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")
            };
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<TournamentSearchResponse>(json, JsonOpts);
            if (parsed?.data is null) break;

            all.AddRange(parsed.data);
            total = parsed.recordsTotal;
            offset += parsed.data.Length;

            if (parsed.data.Length == 0) break; // safety
        }

        return all;
    }

    /// <summary>HTML completo de la página del torneo (necesario para extraer round IDs + formatos).</summary>
    public async Task<string> GetTournamentHtmlAsync(int tournamentId, CancellationToken ct)
    {
        var url = string.Format(MeleeConstants.TournamentPage, tournamentId);
        return await _http.GetStringAsync(url, ct);
    }

    /// <summary>Standings paginados (DataTables) para un round concreto. Devuelve toda la lista.</summary>
    public async Task<List<StandingsItem>> GetRoundStandingsAsync(int roundId, CancellationToken ct)
    {
        var all = new List<StandingsItem>();
        int start = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var body = MeleeConstants.RoundPageParameters
                .Replace("{start}", start.ToString())
                .Replace("{roundId}", roundId.ToString());

            using var req = new HttpRequestMessage(HttpMethod.Post, MeleeConstants.RoundPage)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")
            };
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<StandingsResponse>(json, JsonOpts);
            if (parsed?.data is null || parsed.data.Length == 0) break;

            all.AddRange(parsed.data);
            start += parsed.data.Length;

            // Safety: si todos están vacíos o devuelve menos de 25, hemos terminado.
            if (parsed.data.Length < 25) break;
        }

        return all;
    }

    /// <summary>HTML de una decklist (mainboard, sideboard, per-round results).</summary>
    public async Task<string> GetDeckHtmlAsync(string deckId, CancellationToken ct)
    {
        var url = string.Format(MeleeConstants.DeckPage, deckId);
        return await _http.GetStringAsync(url, ct);
    }
}
