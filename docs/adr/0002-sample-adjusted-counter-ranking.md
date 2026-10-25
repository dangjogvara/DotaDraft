# 0002. Rank counters by sample-adjusted advantage, with k = 30

- **Status:** Accepted
- **Date:** 2026-10-18
- **Issue:** #13 (PR #32)

## Context

Counters were first ranked by raw win rate against the enemy. That had two problems:
- **Small samples won.** 3 wins in 3 games ranked above 5,400 in 10,000.
- **Strong heroes looked like counters to everyone.** A high win rate against one hero mostly reflected the hero's overall strength, not the matchup.

At the time, matchups had a median of about 45 games (the `Pro` source; see [0001](0001-public-matchups-via-explorer.md)).

## Decision

Rank by how much better the counter does against this enemy than it does in general, shrunk towards 0 when there are few games:

```
baseline  = the counter hero's win rate across all its matchups
advantage = (wins − games × baseline) / (games + k)
```

This equals `(winRate − baseline) × games / (games + k)`: the raw edge, scaled by how much the sample can be trusted. `k` is `Counters:PriorGames`, default **30**. The formula lives in `CounterRanking.Advantage()`.

30 is a judgement call, not a tuned value. With it, a matchup needs 30 games to keep half its raw edge and keeps about 60% at the median of 45 games then, so small samples are damped without being thrown away.

## Alternatives considered

- **A minimum-games threshold** (suggested in #13). Rejected: a hard cutoff discards data just below it and still treats 31 games the same as 3,000.
- **The Wilson lower bound on the win rate** (also suggested in #13). Rejected: with about 45 games per matchup, the lower bound pushed nearly every counter below its own baseline, so almost nothing showed a positive advantage.
- **Raw win rate minus baseline, without smoothing.** Rejected: subtracting the baseline fixes strong heroes looking like counters to everyone, but not lucky small samples.

## Consequences

- Responses show `advantage`, `winRate`, `baselineWinRate` and `gamesPlayed`, so users can see why a hero ranks where it does.
- With the public source (a median of about 1,900 games), `games / (games + k)` is close to 1 for most matchups, and `k` mainly damps rarely picked heroes.
- For a lineup, the per-enemy advantages are summed. With small samples, several small-sample edges can add up and lift a hero to the top, so the README tells users to check the game counts.
