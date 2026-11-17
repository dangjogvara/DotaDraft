using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;

namespace DotaDraft.Api.Services;

// The counter-ranking formula. Pure: everything comes in as arguments, so it needs no database to test
public static class CounterRanking
{
    /// <summary>The baseline for a hero without games: no evidence that it's stronger or weaker than average.</summary>
    public const double DefaultBaselineWinRate = 0.5;

    /// <summary>
    /// How much better a hero does against an opponent than it does overall, as a fraction (0.05 = 5 points).
    /// Equivalent to adding <paramref name="priorGames"/> games at the baseline win rate before comparing,
    /// so a lucky 5-of-6 scores far lower than the same win rate over hundreds of games.
    /// </summary>
    /// <param name="baselineWinRate">The hero's overall win rate across all its matchups, as a fraction.</param>
    public static double Advantage(int wins, int gamesPlayed, double baselineWinRate, int priorGames)
    {
        var denominator = gamesPlayed + priorGames;
        return denominator > 0 ? (wins - gamesPlayed * baselineWinRate) / denominator : 0;
    }

    /// <summary>
    /// Scores each hero against the whole enemy lineup: its advantage against each enemy, summed.
    /// Counter suggestions and your team's matchup both come from here, so their numbers always agree.
    /// </summary>
    /// <param name="heroes">The heroes to score. The result keeps their order.</param>
    /// <param name="enemyIds">The enemy lineup. Each hero's per-enemy results keep this order.</param>
    /// <param name="matchups">
    /// Results of the heroes against the enemies. A matchup that's missing or has no games counts as 0 advantage:
    /// no evidence either way. Matchups of other heroes or against other enemies are ignored.
    /// </param>
    /// <param name="baselineWinRates">
    /// Each hero's overall win rate, as a fraction. A hero that's missing gets <see cref="DefaultBaselineWinRate"/>.
    /// </param>
    /// <param name="priorGames">Imaginary games at the baseline added to every matchup; see <see cref="Advantage"/>.</param>
    public static List<HeroScoreDto> Score(
        IReadOnlyList<Hero> heroes,
        IReadOnlyList<int> enemyIds,
        IEnumerable<HeroMatchup> matchups,
        IReadOnlyDictionary<int, double> baselineWinRates,
        int priorGames)
    {
        var played = matchups
            .Where(m => m.GamesPlayed > 0)
            .ToDictionary(m => (m.HeroId, m.AgainstHeroId));

        return heroes
            .Select(hero =>
            {
                var baseline = baselineWinRates.GetValueOrDefault(hero.Id, DefaultBaselineWinRate);

                var perEnemy = enemyIds
                    .Select(enemyId => played.TryGetValue((hero.Id, enemyId), out var m)
                        ? new EnemyMatchupDto(
                            enemyId,
                            ToPoints(Advantage(m.Wins, m.GamesPlayed, baseline, priorGames)),
                            m.WinRate,
                            m.GamesPlayed)
                        : new EnemyMatchupDto(enemyId, 0m, null, 0))
                    .ToList();

                return new HeroScoreDto(
                    hero.Id,
                    hero.LocalizedName,
                    hero.PrimaryAttr,
                    hero.ImgUrl,
                    perEnemy.Sum(e => e.Advantage),
                    ToPoints(baseline),
                    perEnemy);
            })
            .ToList();
    }

    // A fraction as percentage points, rounded to 2 decimals (0.12345 → 12.35)
    private static decimal ToPoints(double fraction) => Math.Round((decimal)fraction * 100m, 2);
}
