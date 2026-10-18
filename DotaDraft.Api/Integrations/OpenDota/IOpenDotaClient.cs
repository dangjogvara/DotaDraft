namespace DotaDraft.Api.Integrations.OpenDota;

public interface IOpenDotaClient
{
    /// <exception cref="OpenDotaException">OpenDota could not be reached or returned unusable data.</exception>
    Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default);

    /// <exception cref="OpenDotaException">OpenDota could not be reached or returned unusable data.</exception>
    Task<IReadOnlyList<OpenDotaMatchupDto>> GetHeroMatchupsAsync(int heroId, CancellationToken ct = default);
}
