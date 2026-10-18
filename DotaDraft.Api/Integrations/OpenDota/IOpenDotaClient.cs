namespace DotaDraft.Api.Integrations.OpenDota;

public interface IOpenDotaClient
{
    /// <exception cref="OpenDotaException">OpenDota could not be reached or returned unusable data.</exception>
    Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default);

    /// <exception cref="OpenDotaException">OpenDota could not be reached or returned unusable data.</exception>
    Task<IReadOnlyList<OpenDotaMatchupDto>> GetHeroMatchupsAsync(int heroId, CancellationToken ct = default);

    /// <summary>The highest match ID in OpenDota's public (non-pro) match table.</summary>
    /// <exception cref="OpenDotaException">OpenDota could not be reached or returned unusable data.</exception>
    Task<long> GetLatestPublicMatchIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Games and wins for every hero pair on opposing teams in ranked All Pick public matches with
    /// IDs in [<paramref name="fromMatchId"/>, <paramref name="toMatchId"/>]. Each pair appears from both sides.
    /// </summary>
    /// <exception cref="OpenDotaException">OpenDota could not be reached, rejected the query (e.g. it timed out) or returned unusable data.</exception>
    Task<IReadOnlyList<OpenDotaPublicMatchupDto>> GetPublicMatchupsAsync(
        long fromMatchId, long toMatchId, int? minAvgRankTier = null, CancellationToken ct = default);
}
