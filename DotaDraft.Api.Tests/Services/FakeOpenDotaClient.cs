using DotaDraft.Api.Integrations.OpenDota;

namespace DotaDraft.Api.Tests.Services;

public class FakeOpenDotaClient : IOpenDotaClient
{
    public List<OpenDotaHeroDto> Heroes { get; } = [];
    public Dictionary<int, List<OpenDotaMatchupDto>> Matchups { get; } = [];

    // Hero IDs whose matchup requests fail as an upstream error
    public HashSet<int> FailingHeroIds { get; } = [];
    public bool FailHeroStats { get; set; }

    public Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default)
    {
        if (FailHeroStats)
            throw new OpenDotaException("Simulated failure.", new HttpRequestException());
        return Task.FromResult<IReadOnlyList<OpenDotaHeroDto>>(Heroes);
    }

    public Task<IReadOnlyList<OpenDotaMatchupDto>> GetHeroMatchupsAsync(int heroId, CancellationToken ct = default)
    {
        if (FailingHeroIds.Contains(heroId))
            throw new OpenDotaException("Simulated failure.", new HttpRequestException());
        return Task.FromResult<IReadOnlyList<OpenDotaMatchupDto>>(Matchups.GetValueOrDefault(heroId) ?? []);
    }
}
