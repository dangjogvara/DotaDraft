# 0005. Seed matchups in a background job with in-memory status

- **Status:** Accepted
- **Date:** 2026-10-18
- **Issue:** #14 (PR #26)

## Context

Seeding matchups takes minutes, because OpenDota's free tier allows about one request a second (now about 1.5 minutes with the public source). Running it inside the HTTP request meant a proxy timeout or a client disconnect could cancel it halfway.

## Decision

`POST /api/admin/seed-matchups` only queues a run and returns `202 Accepted`:
- `MatchupSeedJob` (a singleton) holds a channel with room for one request, plus the latest run's status. A second request while one is queued or running gets `409 Conflict`.
- `MatchupSeedWorker` (a `BackgroundService`) takes requests from the channel and runs each one in its own DI scope, since the ingestion services and `AppDbContext` are scoped.
- Clients poll `GET /api/admin/seed-matchups/status`.

Seeding heroes stays a normal request, because it takes about a second.

## Alternatives considered

- **Keep seeding inside the request.** Rejected: it's cancelled by timeouts and disconnects.
- **A separate CLI command for seeding** (suggested in #14). Rejected for now: a deployed API would need a second way to run code against its database, while an endpoint works anywhere the API runs.
- **A job library such as Hangfire or Quartz, or a database table for job status.** Rejected for now: there is one kind of job, one run at a time and one API instance, so the built-in `Channel` and `BackgroundService` are enough.

## Consequences

- The status is lost when the API restarts, and a run in progress is marked failed when the app shuts down.
- It assumes a single API instance: with several, each would have its own queue and status. Running more than one instance would mean storing job state in the database.
- Scheduled seeding (also suggested in #14) isn't built. Seeds run when an admin asks for one.
