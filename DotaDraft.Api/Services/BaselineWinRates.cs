using DotaDraft.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Services;

public static class BaselineWinRates
{
    /// <summary>
    /// Each hero's overall win rate across all its matchups, as a fraction, so general strength
    /// isn't mistaken for countering. Heroes without games default to 0.5.
    /// </summary>
    public static Task<Dictionary<int, double>> LoadBaselineWinRatesAsync(this AppDbContext db, CancellationToken ct) =>
        db.HeroMatchups
            .AsNoTracking()
            .GroupBy(m => m.HeroId)
            .Select(g => new { HeroId = g.Key, Wins = g.Sum(m => m.Wins), Games = g.Sum(m => m.GamesPlayed) })
            .ToDictionaryAsync(b => b.HeroId, b => b.Games > 0 ? (double)b.Wins / b.Games : 0.5, ct);
}
