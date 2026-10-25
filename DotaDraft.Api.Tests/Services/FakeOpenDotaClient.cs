using DotaDraft.Api.Integrations.OpenDota;

namespace DotaDraft.Api.Tests.Services;

public class FakeOpenDotaClient : IOpenDotaClient
{
    public List<OpenDotaHeroDto> Heroes { get; } = [];
    public bool FailHeroStats { get; set; }
    // Thrown as-is, to simulate a bug rather than an upstream failure
    public Exception? HeroStatsBug { get; set; }
    public int HeroStatsRequests { get; private set; }

    public long LatestPublicMatchId { get; set; } = 1_000_000;
    public bool FailLatestPublicMatchId { get; set; }

    // Explorer rows per window, keyed by the window's highest match ID
    public Dictionary<long, List<OpenDotaPublicMatchupDto>> PublicMatchups { get; } = [];
    public HashSet<long> FailingWindows { get; } = [];
    public List<(long From, long To, int? MinAvgRankTier)> PublicMatchupRequests { get; } = [];

    public Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default)
    {
        HeroStatsRequests++;
        if (HeroStatsBug is not null)
            throw HeroStatsBug;
        if (FailHeroStats)
            throw new OpenDotaException("Simulated failure.", new HttpRequestException());
        return Task.FromResult<IReadOnlyList<OpenDotaHeroDto>>(Heroes);
    }

    public Task<long> GetLatestPublicMatchIdAsync(CancellationToken ct = default)
    {
        if (FailLatestPublicMatchId)
            throw new OpenDotaException("Simulated failure.", new HttpRequestException());
        return Task.FromResult(LatestPublicMatchId);
    }

    public Task<IReadOnlyList<OpenDotaPublicMatchupDto>> GetPublicMatchupsAsync(
        long fromMatchId, long toMatchId, int? minAvgRankTier = null, CancellationToken ct = default)
    {
        PublicMatchupRequests.Add((fromMatchId, toMatchId, minAvgRankTier));
        if (FailingWindows.Contains(toMatchId))
            throw new OpenDotaException("Simulated failure.", new HttpRequestException());
        return Task.FromResult<IReadOnlyList<OpenDotaPublicMatchupDto>>(PublicMatchups.GetValueOrDefault(toMatchId) ?? []);
    }
}
