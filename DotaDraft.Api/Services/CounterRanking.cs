using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;

namespace DotaDraft.Api.Services;

// The counter-ranking formula. Pure: everything comes in as arguments, so it needs no database to test
public static class CounterRanking
{
    /// <summary>
    /// A hero's win rate across all its matchups, as a fraction, so general strength isn't mistaken for countering.
    /// <see cref="Hero.DefaultBaselineWinRate"/> without games.
    /// </summary>
    public static double BaselineWinRate(long wins, long gamesPlayed) =>
        gamesPlayed > 0 ? (double)wins / gamesPlayed : Hero.DefaultBaselineWinRate;

    /// <summary>
    /// The win rate a hero would have against an enemy if neither countered the other: the hero's baseline, moved by
    /// how far the enemy's baseline is from 50%. Against an enemy that wins 55% in general, a 50% hero expects 45%.
    /// </summary>
    /// <remarks>
    /// Additive rather than log-odds (log5): at real baselines (about 42–56%) the two differ by at most about
    /// 0.2 points, and this one can be checked by hand. Clamped to 0–1 for extreme baselines.
    /// </remarks>
    public static double ExpectedWinRate(double baselineWinRate, double enemyBaselineWinRate) =>
        Math.Clamp(baselineWinRate - (enemyBaselineWinRate - Hero.DefaultBaselineWinRate), 0, 1);

    /// <summary>
    /// How much better a hero does against an opponent than both heroes' general strength predicts, as a fraction
    /// (0.05 = 5 points). Equivalent to adding <paramref name="priorGames"/> games at the expected win rate before
    /// comparing, so a lucky 5-of-6 scores far lower than the same win rate over hundreds of games.
    /// </summary>
    /// <param name="expectedWinRate">What the hero would win against this opponent without a counter effect; see <see cref="ExpectedWinRate"/>.</param>
    public static double Advantage(int wins, int gamesPlayed, double expectedWinRate, int priorGames)
    {
        var denominator = gamesPlayed + priorGames;
        return denominator > 0 ? (wins - gamesPlayed * expectedWinRate) / denominator : 0;
    }

    /// <summary>
    /// Scores each hero against the whole enemy lineup: its advantage against each enemy, summed.
    /// Each advantage accounts for both the hero's and that enemy's baseline win rate.
    /// Counter suggestions and your team's matchup both come from here, so their numbers always agree.
    /// </summary>
    /// <param name="heroes">The heroes to score, with their baseline win rates. The result keeps their order.</param>
    /// <param name="enemyIds">The enemy lineup. Each hero's per-enemy results keep this order.</param>
    /// <param name="enemyBaselines">
    /// The enemies' baseline win rates by ID, as fractions. A missing enemy gets <see cref="Hero.DefaultBaselineWinRate"/>,
    /// which leaves the hero's expected win rate at its own baseline.
    /// </param>
    /// <param name="matchups">
    /// Results of the heroes against the enemies. A matchup that's missing or has no games counts as 0 advantage:
    /// no evidence either way. Matchups of other heroes or against other enemies are ignored.
    /// </param>
    /// <param name="priorGames">Imaginary games at the baseline added to every matchup; see <see cref="Advantage"/>.</param>
    public static List<HeroScoreDto> Score(
        IReadOnlyList<Hero> heroes,
        IReadOnlyList<int> enemyIds,
        IReadOnlyDictionary<int, double> enemyBaselines,
        IEnumerable<HeroMatchup> matchups,
        int priorGames)
    {
        var played = matchups
            .Where(m => m.GamesPlayed > 0)
            .ToDictionary(m => (m.HeroId, m.AgainstHeroId));

        return heroes
            .Select(hero =>
            {
                var baseline = hero.BaselineWinRate;
                double EnemyBaseline(int enemyId) => enemyBaselines.GetValueOrDefault(enemyId, Hero.DefaultBaselineWinRate);

                var perEnemy = enemyIds
                    .Select(enemyId =>
                    {
                        if (!played.TryGetValue((hero.Id, enemyId), out var m))
                            return new EnemyMatchupDto(enemyId, 0m, null, null, 0);

                        var expected = ExpectedWinRate(baseline, EnemyBaseline(enemyId));
                        return new EnemyMatchupDto(
                            enemyId,
                            ToPoints(Advantage(m.Wins, m.GamesPlayed, expected, priorGames)),
                            m.WinRate,
                            ToPoints(expected),
                            m.GamesPlayed);
                    })
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
