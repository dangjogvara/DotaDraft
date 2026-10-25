# 0003. Use EF Core's `DbContext` directly, without a repository layer

- **Status:** Accepted
- **Date:** 2026-10-23
- **Issue:** #60 (PR #67)

## Context

When the endpoint logic moved into services (`CounterService`, `HeroIngestionService`, the matchup sources), the question was what those services should use to reach the database. A common pattern is a repository layer: an `IHeroRepository` with methods like `GetById`, wrapping the database code.

## Decision

Services take `AppDbContext` and query it directly. There is no repository layer, no unit-of-work wrapper, and no MediatR or split into several projects.

## Alternatives considered

- **Repositories over EF Core.** Rejected. `DbContext` already is a unit of work, and each `DbSet` is already a repository. A layer on top would either:
  - hide `IQueryable`, so every new query shape (projections, `AsNoTracking`, grouping for baselines) needs a new repository method, or
  - expose `IQueryable`, adding a layer that hides nothing.
- **Repositories to make services testable.** Rejected: services are tested against EF Core's in-memory database instead (see the `Services/` tests), which needs no extra interfaces.
- **MediatR, or separate projects per layer.** Rejected: the API has six endpoints and a few services, and both would add indirection without solving a problem it has.

## Consequences

- Services contain EF Core code, such as `ChangeTracker` handling in the seeding sources. Changing ORM would mean changing the services, which is unlikely for this app.
- The in-memory provider doesn't behave exactly like Postgres (no transactions or SQL translation), so database-specific behaviour is checked by running the API against the local Postgres, as CLAUDE.md describes.
