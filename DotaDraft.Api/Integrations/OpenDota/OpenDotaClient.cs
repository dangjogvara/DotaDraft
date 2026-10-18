using System.Globalization;
using System.Text.Json;

namespace DotaDraft.Api.Integrations.OpenDota;

public class OpenDotaClient(HttpClient httpClient) : IOpenDotaClient
{
    // Ranked All Pick: game_mode 22, lobby_type 7
    private const string RankedAllPickFilter = "game_mode = 22 AND lobby_type = 7";

    public Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default) =>
        GetListAsync<OpenDotaHeroDto>("api/heroStats", ct);

    public Task<IReadOnlyList<OpenDotaMatchupDto>> GetHeroMatchupsAsync(int heroId, CancellationToken ct = default) =>
        GetListAsync<OpenDotaMatchupDto>($"api/heroes/{heroId}/matchups", ct);

    public async Task<long> GetLatestPublicMatchIdAsync(CancellationToken ct = default)
    {
        var rows = await QueryExplorerAsync<OpenDotaMatchIdRow>("SELECT max(match_id) AS match_id FROM public_matches", ct);
        return rows.FirstOrDefault()?.MatchId
               ?? throw new OpenDotaException("OpenDota returned no public matches.", new InvalidDataException());
    }

    public Task<IReadOnlyList<OpenDotaPublicMatchupDto>> GetPublicMatchupsAsync(
        long fromMatchId, long toMatchId, int? minAvgRankTier = null, CancellationToken ct = default)
    {
        // Filter on match_id (the primary key): a start_time filter isn't indexed and hits the explorer's
        // ~15s statement timeout. Matches with a hidden or missing hero (ID 0) are skipped entirely.
        // Only numbers are interpolated, so the SQL can't be injected into.
        var rankFilter = minAvgRankTier is { } tier
            ? $"AND avg_rank_tier >= {tier.ToString(CultureInfo.InvariantCulture)}"
            : "";
        var sql = $"""
            WITH m AS (
              SELECT radiant_win, radiant_team, dire_team FROM public_matches
              WHERE match_id BETWEEN {fromMatchId.ToString(CultureInfo.InvariantCulture)} AND {toMatchId.ToString(CultureInfo.InvariantCulture)}
                AND {RankedAllPickFilter} {rankFilter}
                AND cardinality(radiant_team) = 5 AND cardinality(dire_team) = 5
                AND NOT 0 = ANY(radiant_team) AND NOT 0 = ANY(dire_team)
            ), p AS (
              SELECT r AS hero_id, d AS against_hero_id, radiant_win AS won FROM m, unnest(radiant_team) r, unnest(dire_team) d
              UNION ALL
              SELECT d, r, NOT radiant_win FROM m, unnest(radiant_team) r, unnest(dire_team) d
            )
            SELECT hero_id, against_hero_id, count(*) AS games, sum(won::int) AS wins FROM p GROUP BY 1, 2
            """;
        return QueryExplorerAsync<OpenDotaPublicMatchupDto>(sql, ct);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string requestUri, CancellationToken ct)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<List<T>>(requestUri, ct) ?? [];
        }
        catch (Exception ex) when (IsUpstreamFailure(ex, ct))
        {
            throw new OpenDotaException($"OpenDota request to '{requestUri}' failed.", ex);
        }
    }

    // /api/explorer runs read-only SQL over OpenDota's database. It isn't a stable, documented API:
    // a schema change on their side shows up here as a 400 with an "err" message
    private async Task<IReadOnlyList<T>> QueryExplorerAsync<T>(string sql, CancellationToken ct)
    {
        try
        {
            using var response = await httpClient.GetAsync($"api/explorer?sql={Uri.EscapeDataString(sql)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Explorer query failed with {(int)response.StatusCode}: {ReadExplorerError(body)}",
                    null, response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<OpenDotaExplorerResponse<T>>(ct);
            return result?.Rows ?? throw new JsonException("Explorer response has no rows.");
        }
        catch (Exception ex) when (IsUpstreamFailure(ex, ct))
        {
            throw new OpenDotaException("OpenDota explorer query failed.", ex);
        }
    }

    private static string ReadExplorerError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("err", out var err))
                return err.ToString();
        }
        catch (JsonException)
        {
        }

        return body.Length > 200 ? body[..200] : body;
    }

    // HTTP errors, malformed JSON and HttpClient timeouts are OpenDota's fault.
    // Cancellation by the caller is not, and must propagate.
    private static bool IsUpstreamFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
