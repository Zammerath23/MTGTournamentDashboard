using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MTGTournamentDashboard.Sync.Models;

namespace MTGTournamentDashboard.Sync.Melee;

internal static class MeleeHtmlParser
{
    /// <summary>Parsea la página de torneo. Devuelve round IDs completados y formatos declarados.</summary>
    public static (int[] RoundIds, string[] Formats) ParseTournamentPage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var roundNodes = doc.DocumentNode.SelectNodes(
            "//button[contains(@class,'round-selector') and @data-is-completed='True']");
        var roundIds = roundNodes?
            .Select(n => n.Attributes["data-id"]?.Value)
            .Where(v => int.TryParse(v, out _))
            .Select(v => int.Parse(v!))
            .ToArray() ?? Array.Empty<int>();

        var infoNode = doc.DocumentNode.SelectSingleNode("//p[@id='tournament-headline-registration']");
        string[] formats;
        if (infoNode is null)
        {
            formats = Array.Empty<string>();
        }
        else
        {
            var text = infoNode.InnerText;
            var pipes = text.Split('|');
            if (pipes.Length >= 3)
            {
                formats = pipes[2].Replace("Format:", "").Split(',')
                    .Select(f => f.Trim())
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToArray();
            }
            else
            {
                formats = Array.Empty<string>();
            }
        }
        return (roundIds, formats);
    }

    /// <summary>Resultado de parsear una página de decklist.</summary>
    public sealed record ParsedDeck(
        CacheDeckItem[] Mainboard,
        CacheDeckItem[] Sideboard,
        string? Format,
        List<ParsedDeckRound> Rounds);

    public sealed record ParsedDeckRound(string RoundName, string OpponentName, string ResultText);

    public static ParsedDeck ParseDeckPage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Mainboard / sideboard: el botón "copy" contiene el listado de cartas como texto plano.
        var copyButton = doc.DocumentNode.SelectSingleNode(
            "//button[contains(@class,'decklist-builder-copy-button')]");
        var cardList = copyButton?.Attributes["data-clipboard-text"]?.Value;
        var (main, side) = ParseCardList(WebUtility.HtmlDecode(cardList ?? ""));

        // Formato declarado dentro de la tarjeta del deck.
        string? format = null;
        var formatNode = doc.DocumentNode.SelectSingleNode(
            "//div[contains(@class,'decklist-card-header')]//div//div[contains(@class,'format')]");
        if (formatNode is not null)
        {
            format = NormalizeSpaces(formatNode.InnerText);
        }

        // Per-round table: filas dentro de tournament-path-grid-item.
        var rounds = new List<ParsedDeckRound>();
        var grid = doc.DocumentNode.SelectSingleNode("//div[@id='tournament-path-grid-item']");
        if (grid is not null)
        {
            var rows = grid.SelectNodes(".//table/tbody/tr");
            if (rows is not null)
            {
                foreach (var row in rows)
                {
                    var cols = row.SelectNodes("td");
                    if (cols is null || cols.Count < 4) continue;

                    var roundName = NormalizeSpaces(WebUtility.HtmlDecode(cols[0].InnerHtml));
                    if (string.Equals(roundName, "No results found", StringComparison.OrdinalIgnoreCase)) continue;

                    var oppNode = cols[1].SelectSingleNode("a");
                    var opp = NormalizeSpaces(WebUtility.HtmlDecode(oppNode?.InnerHtml ?? cols[1].InnerText ?? ""));
                    var result = NormalizeSpaces(WebUtility.HtmlDecode(cols[3].InnerHtml));

                    rounds.Add(new ParsedDeckRound(roundName, opp, result));
                }
            }
        }

        return new ParsedDeck(main, side, format, rounds);
    }

    private static (CacheDeckItem[] Main, CacheDeckItem[] Side) ParseCardList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (Array.Empty<CacheDeckItem>(), Array.Empty<CacheDeckItem>());

        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var main = new List<CacheDeckItem>();
        var side = new List<CacheDeckItem>();
        bool inSideboard = false;
        bool inCompanion = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            switch (trimmed)
            {
                case "Deck":      inSideboard = false; inCompanion = false; continue;
                case "Sideboard": inSideboard = true;  inCompanion = false; continue;
                case "Companion": inSideboard = false; inCompanion = true;  continue;
            }

            if (inCompanion) continue; // El companion aparece también en sideboard, se descarta aquí.

            var space = trimmed.IndexOf(' ');
            if (space <= 0) continue;
            if (!int.TryParse(trimmed[..space], out var count)) continue;
            var name = trimmed[(space + 1)..].Trim();
            if (name.Length == 0) continue;

            (inSideboard ? side : main).Add(new CacheDeckItem { Count = count, CardName = name });
        }
        return (main.ToArray(), side.ToArray());
    }

    private static string NormalizeSpaces(string? s) =>
        s is null ? "" : Regex.Replace(s, @"\s+", " ").Trim();
}
