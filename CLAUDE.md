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
- A full matchup seed takes about 1.5 minutes (about 16 OpenDota requests; 3 minutes and 128 requests with `Matchups:Source=Pro`) and replaces the whole matchup table, so only run one when the change needs it.

## Code conventions

- `Endpoints/`: one static class per area, registered from `Program.cs`. Responses use `TypedResults`. Every error is ProblemDetails: return `TypedResults.Problem`/`ValidationProblem`; empty status codes and unhandled exceptions are converted by `UseStatusCodePages`/`UseExceptionHandler` (exception details only in Development).
- `Dtos/` holds this API's response contracts. Return DTOs, never EF entities.
- `Integrations/OpenDota/`: the typed client (`IOpenDotaClient`), its DTOs and options. It's registered via `AddOpenDotaClient()`, which includes the retry pipeline. It throws `OpenDotaException` for upstream failures.
- `Services/`: `HeroIngestionService` (scoped seeding: per-hero upserts for `Pro`, a windowed whole-table replace for `Public`), `MatchupSeedJob`/`MatchupSeedWorker` (background seeding), `CounterRanking`.
- Request validation uses data annotations on handler parameters (`AddValidation()`); only checks that need the database stay in the handler.
- Settings are options classes with a `SectionName`, documented defaults and data-annotation ranges, registered with `ValidateDataAnnotations().ValidateOnStart()`. Keep `appsettings.json` and the README's configuration table in sync.
- Frontend: standalone components, signals, and Tailwind utility classes in templates. API calls go through `HeroApiService` using `environment.apiBaseUrl`. The interfaces in `hero.models.ts` mirror the C# DTOs, so change both together.
- Tests:
  - Frontend: `provideHttpClientTesting()` with `HttpTestingController`.
  - `DotaDraftApiFactory` for HTTP-level tests: the real app with an in-memory DB and `FakeOpenDotaClient`.
  - The in-memory DB directly for service tests.
  - `ServiceCollection` + `AddOpenDotaClient()` with a stub `HttpMessageHandler` for client and retry tests.

## Secrets

- The connection string, `Admin:ApiKey` and `OpenDota:ApiKey` live in user-secrets. The Postgres password lives in the gitignored `.env`. Never commit them.
- Never print secret values: hide them when listing (`dotnet user-secrets list --project DotaDraft.Api | sed -E 's/ = .*/ = <hidden>/'`), and pass them through shell variables.
- Admin endpoints need the `X-Admin-Key` header:
  ```bash
  KEY="$(dotnet user-secrets list --project DotaDraft.Api | sed -n 's/^Admin:ApiKey = //p')"
  ```

## Gotchas

- `docker exec` needs `-i` to receive SQL on stdin (heredocs). Without it, psql silently runs nothing.
- To stop a background API, use `kill -TERM $(pgrep -x DotaDraft.Api)`. To stop `ng serve`, use `kill -TERM $(lsof -ti tcp:4200)`. Never use `pkill -f` or `pgrep -f` with a pattern that appears in your own command, because it matches and kills the shell running it. SIGINT is ignored by processes started in the background.
- OpenDota's free tier allows 60 requests/minute and roughly 3,000/day, and still sends occasional bursts of 429s. Its OpenAPI spec is at `https://api.opendota.com/api`. Public matchups come from SQL sent to `/explorer`: filter `public_matches` by `match_id` range (a `start_time` filter times out), and keep each query under its ~15s statement timeout. The `Pro` source has small samples (a median of about 45 games per matchup).
- Before applying a migration to the local DB, check `__EFMigrationsHistory`. The local DB once had an orphan migration that was never committed.
