# DotaDraft

Dota 2 counter-pick app: an ASP.NET Core Minimal API (.NET 10) with PostgreSQL via EF Core and data from the OpenDota API, plus an Angular 21 client with Tailwind CSS v4 in `DotaDraft.Web/`. Setup, endpoints, configuration and the counter-ranking formula are in [README.md](README.md).

## Commands

```bash
dotnet build
dotnet test                                                    # no DB, Docker or secrets needed
dotnet run --project DotaDraft.Api --launch-profile http       # http://localhost:8080
dotnet tool restore                                            # once: installs the pinned dotnet-ef
dotnet ef migrations add <Name> --project DotaDraft.Api
dotnet ef database update --project DotaDraft.Api
docker compose up -d                                           # Postgres, container dota_postgres

cd DotaDraft.Web
npm start                                                      # ng serve, http://localhost:4200
npm test -- --watch=false                                      # Vitest
npx ng build
```

## Workflow

- Work from GitHub issues: one issue per change. If there's no issue for a change, open one first (`gh issue create --assignee @me` with a label).
- Branch off an up-to-date `main` as `<type>/<short-slug>-<issue#>`, e.g. `fix/rate-limit-delay-24`.
- Commit messages: `<type>: <summary> (#<issue>)`, with types `feat`, `fix`, `refactor`, `chore`, `docs`.
- PR body: `Closes #<issue>`, a Changes section and a Testing section that says what was verified, including anything not verified.
- CI (`.github/workflows/ci.yml`) must pass before merging. Check it with `gh pr checks <pr> --watch`.
- Merge with `gh pr merge --squash --delete-branch`, then return to `main`.
- Ask before committing, opening a PR or merging.

## Verifying changes

- Add tests for behaviour changes, and check that a new test fails against the old code.
- For changes to endpoints, seeding or config, also run the API against the local Postgres and exercise the change with curl.
- Seeding runs in the background, every `Seeding:IntervalHours` counted from the last successful seed (stored in `SeedingStates`), so at startup only when `HeroMatchups` is empty or the last seed is older than the interval. A full seed takes about 1.5 minutes (about 16 OpenDota requests) and replaces the whole matchup table, so only trigger one when the change needs it: truncate `HeroMatchups` and restart the API (see the README's Seeding section).

## Code conventions

- One top-level type (class, record, enum or interface) per file, named after the type, e.g. `SeedingWorker` in `SeedingWorker.cs`.
- `Endpoints/`: one static class per area, registered from `Program.cs`. Handlers return `TypedResults` with typed return types (`Results<Ok<T>, ProblemHttpResult>`), and are mapped with `WithName`, `WithTags` and `ProducesProblem` for status codes the types can't express. Handlers are `internal`, not `private`, so their `///` summaries reach the OpenAPI document (`/openapi/v1.json`, browsable at `/scalar` in Development). Every error is ProblemDetails: return `TypedResults.Problem`/`ValidationProblem`; empty status codes and unhandled exceptions are converted by `UseStatusCodePages`/`UseExceptionHandler` (exception details only in Development).
- `Dtos/` holds this API's response contracts. Return DTOs, never EF entities.
- `Integrations/OpenDota/`: the typed client (`IOpenDotaClient`), its DTOs and options. It's registered via `AddOpenDotaClient()`, which includes the retry pipeline. It throws `OpenDotaException` for upstream failures.
- `Services/`: `CounterService` (the counter query behind `/api/counters`; `CounterRanking` holds the formula), `HeroIngestionService` (scoped seeding of heroes, and of matchups from public matches via `/explorer` as a windowed whole-table replace), `SeedingWorker` (a `BackgroundService` that seeds heroes then matchups when a seed is due, records when it finished in `SeedingStates`, and waits with `Task.Delay` on a `TimeProvider`, so tests use `FakeTimeProvider`).
- Endpoints stay thin: bind, call a service, map the result to `TypedResults`. The exception is a handler that is a single read-only query with a projection to a DTO (like `GetHeroes`): it can use `AppDbContext` directly. Move it into a service once it gains validation, business logic or a second caller. No repository layer: `AppDbContext` is used directly in services and in those simple handlers.
- Request validation uses data annotations on handler parameters (`AddValidation()`); checks that need the database go in the service, and the handler maps their result to a `ValidationProblem` or `Problem`.
- Settings are options classes with a `SectionName`, documented defaults and data-annotation ranges, registered in `AddDotaDraftOptions()` (`OptionsServiceCollectionExtensions.cs`), which validates them at startup. `OptionsValidationTests` checks that validation through `IStartupValidator` without starting the app. Keep `appsettings.json` and the README's configuration table in sync.
- Frontend: standalone components, signals, and Tailwind utility classes in templates. API calls go through `HeroApiService` using `environment.apiBaseUrl`. The interfaces in `hero.models.ts` mirror the C# DTOs, so change both together.
- Tests:
  - Frontend: `provideHttpClientTesting()` with `HttpTestingController`.
  - `DotaDraftApiFactory` for HTTP-level tests: the real app with an in-memory DB and `FakeOpenDotaClient`.
  - The in-memory DB directly for service tests.
  - `ServiceCollection` + `AddOpenDotaClient()` with a stub `HttpMessageHandler` for client and retry tests.

## Secrets

- The connection string and `OpenDota:ApiKey` live in user-secrets. The Postgres password lives in the gitignored `.env`. Never commit them.
- Never print secret values: hide them when listing (`dotnet user-secrets list --project DotaDraft.Api | sed -E 's/ = .*/ = <hidden>/'`), and pass them through shell variables.

## Gotchas

- `docker exec` needs `-i` to receive SQL on stdin (heredocs). Without it, psql silently runs nothing.
- To stop a background API, use `kill -TERM $(pgrep -x DotaDraft.Api)`. To stop `ng serve`, use `kill -TERM $(lsof -ti tcp:4200)`. Never use `pkill -f` or `pgrep -f` with a pattern that appears in your own command, because it matches and kills the shell running it. SIGINT is ignored by processes started in the background.
- OpenDota's free tier allows 60 requests/minute and roughly 3,000/day, and still sends occasional bursts of 429s. Its OpenAPI spec is at `https://api.opendota.com/api`. Public matchups come from SQL sent to `/explorer`: filter `public_matches` by `match_id` range (a `start_time` filter times out), and keep each query under its ~15s statement timeout.
- Before applying a migration to the local DB, check `__EFMigrationsHistory`. The local DB once had an orphan migration that was never committed.
