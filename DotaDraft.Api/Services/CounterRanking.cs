namespace DotaDraft.Api.Services;

public static class CounterRanking
{
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
}
