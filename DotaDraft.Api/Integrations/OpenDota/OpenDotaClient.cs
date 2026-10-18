using System.Text.Json;

namespace DotaDraft.Api.Integrations.OpenDota;

public class OpenDotaClient(HttpClient httpClient) : IOpenDotaClient
{
    public Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default) =>
        GetListAsync<OpenDotaHeroDto>("api/heroStats", ct);

    public Task<IReadOnlyList<OpenDotaMatchupDto>> GetHeroMatchupsAsync(int heroId, CancellationToken ct = default) =>
        GetListAsync<OpenDotaMatchupDto>($"api/heroes/{heroId}/matchups", ct);

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

    // HTTP errors, malformed JSON and HttpClient timeouts are OpenDota's fault.
    // Cancellation by the caller is not, and must propagate.
    private static bool IsUpstreamFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
