namespace DotaDraft.Api.Integrations.OpenDota;

/// <summary>Every method throws <see cref="OpenDotaException"/> when OpenDota fails or returns unusable data.</summary>
public interface IOpenDotaClient
{
    Task<IReadOnlyList<OpenDotaHeroDto>> GetHeroStatsAsync(CancellationToken ct = default);

    /// <summary>The highest match ID in OpenDota's public (non-pro) match table.</summary>
    Task<long> GetLatestPublicMatchIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Games and wins for every hero pair on opposing teams in ranked All Pick matches with IDs in the range, from
    /// both sides.
    /// </summary>
    Task<IReadOnlyList<OpenDotaPublicMatchupDto>> GetPublicMatchupsAsync(
        long fromMatchId, long toMatchId, int? minAvgRankTier = null, CancellationToken ct = default);
}
