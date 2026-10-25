# Architecture decision records

Each record explains one decision that shaped the code: the problem, what was chosen and why, the alternatives that were rejected, and what follows from it. They're for anyone wondering why the code looks the way it does, before changing it.

| # | Decision | Date |
|---|---|---|
| [0001](0001-public-matchups-via-explorer.md) | Matchups from public ranked matches via OpenDota's `/explorer` | 2026-10-18 |
| [0002](0002-sample-adjusted-counter-ranking.md) | Rank counters by sample-adjusted advantage, with k = 30 | 2026-10-18 |
| [0003](0003-ef-core-without-repositories.md) | Use EF Core's `DbContext` directly, without a repository layer | 2026-10-23 |
| [0004](0004-admin-api-key.md) | Protect the admin endpoints with one shared API key | 2026-10-18 |
| [0005](0005-background-seeding-with-in-memory-status.md) | Seed matchups in a background job with in-memory status | 2026-10-18 |

## Writing a new record

Copy the shape of an existing one: **Context**, **Decision**, **Alternatives considered** and **Consequences**, plus a status. Number records in order and never reuse a number. When a decision changes, don't rewrite the old record: add a new one and set the old one's status to "Superseded by 00NN".
