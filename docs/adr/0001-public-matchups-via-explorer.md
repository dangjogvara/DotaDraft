# 0001. Matchups from public ranked matches via OpenDota's `/explorer`

- **Status:** Accepted
- **Date:** 2026-10-18
- **Issue:** #46 (PR #53)

## Context

The first version read matchups from OpenDota's `/heroes/{id}/matchups`, one request per hero. That endpoint uses a small set of high-level matches: the median matchup had about 45 games, so counter scores were noisy and well-known counters ranked low (Anti-Mage was 119th against Medusa).

OpenDota's `/explorer` endpoint runs SQL against its `public_matches` table, which has far more games. It has limits:
- Queries stop after about 15 seconds.
- Filtering on `start_time` times out, but filtering on a `match_id` range (the primary key) is fast.
- About 19% of public matches have hero ID `0` (hidden or missing) and have to be skipped.

## Decision

Use public ranked All Pick matches from `/explorer` as the default source (`Matchups:Source` = `Public`):
- Walk back from the newest match in windows of `WindowSize` match IDs (default 200,000; 15 windows, about 1.5 days), so each query stays under the timeout.
- Add the windows' totals together and replace the whole matchup table in one save.
- Write nothing if fewer than half the windows succeed, so a bad run can't swap good data for a tiny sample.

Keep the per-hero endpoint as the `Pro` source, selectable in configuration, as a fallback.

## Alternatives considered

- **Keep only `/heroes/{id}/matchups`.** Rejected: about 45 games per matchup is too few to separate real counters from luck.
- **Filter public matches by date (`start_time`).** Rejected: those queries time out on OpenDota's side.
- **One explorer query per hero.** Rejected in favour of one query per window covering every hero pair: about 16 requests instead of one per hero.

## Consequences

- The median matchup now has about 1,900 games instead of 45, and known counters rank near the top.
- `/explorer` is raw SQL against OpenDota's schema, not a stable, documented API. If the schema changes, seeding fails, and switching to `Pro` is the fallback.
- A public seed replaces the whole table, while a `Pro` seed upserts per hero. The two sources live in separate classes behind `IMatchupSource` (see `Services/MatchupSources/`).
- With large samples, the ranking's smoothing ([0002](0002-sample-adjusted-counter-ranking.md)) matters much less.
