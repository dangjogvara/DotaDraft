# DotaDraft

A Dota 2 draft helper. Given an enemy hero, DotaDraft suggests the heroes that counter it best, using hero matchup data from the [OpenDota API](https://docs.opendota.com).

The backend is an ASP.NET Core Minimal API with PostgreSQL. The frontend is an Angular app styled with Tailwind CSS (`DotaDraft.Web/`).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/) (for PostgreSQL)
- EF Core CLI: `dotnet tool install --global dotnet-ef`
- [Node.js](https://nodejs.org/) 20.19+ (for the frontend)

## Local setup

Secrets never live in the repo. The database password goes in a gitignored `.env` file (read by Docker Compose), and the app's connection string and keys go in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), which are stored outside the project folder in `~/.microsoft/usersecrets/`.

1. **Database password.** Copy the template and choose a password:

   ```bash
   cp .env.example .env
   ```

2. **Start PostgreSQL:**

   ```bash
   docker compose up -d
   ```

3. **App secrets.** Use the same password as in `.env`:

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
     "Host=localhost;Port=5432;Database=dotadraft;Username=postgres;Password=<your password>" \
     --project DotaDraft.Api

   # Required for the admin (seeding) endpoints; without it they are disabled
   dotnet user-secrets set "Admin:ApiKey" "$(openssl rand -hex 32)" --project DotaDraft.Api

   # Optional: an OpenDota API key (https://www.opendota.com/api-keys) for higher rate limits
   read -rsp "OpenDota API key: " KEY && echo && dotnet user-secrets set "OpenDota:ApiKey" "$KEY" --project DotaDraft.Api; unset KEY
   ```

   Run `dotnet user-secrets list --project DotaDraft.Api` to see the values later.

4. **Create the schema:**

   ```bash
   dotnet ef database update --project DotaDraft.Api
   ```

5. **Run the API** on http://localhost:8080:

   ```bash
   dotnet run --project DotaDraft.Api --launch-profile http
   ```

6. **Seed the data** (see below).

7. **Run the frontend** on http://localhost:4200, in a second terminal:

   ```bash
   cd DotaDraft.Web
   npm install
   npm start          # ng serve
   ```

   In development it calls the API at `http://localhost:8080` (set in `src/environments/environment.development.ts`). CORS already allows the dev server's origin.

## Seeding

The database starts empty. Seed **heroes first, then matchups**, because matchups reference heroes. Admin endpoints need your admin key in the `X-Admin-Key` header:

```bash
ADMIN_KEY="$(dotnet user-secrets list --project DotaDraft.Api | sed -n 's/^Admin:ApiKey = //p')"

# 1. Heroes: about a second
curl -X POST -H "X-Admin-Key: $ADMIN_KEY" http://localhost:8080/api/admin/seed-heroes

# 2. Matchups: queued as a background job (202 Accepted), takes about 3 minutes
curl -X POST -H "X-Admin-Key: $ADMIN_KEY" http://localhost:8080/api/admin/seed-matchups

# Poll until "state" is Succeeded or Failed
curl -H "X-Admin-Key: $ADMIN_KEY" http://localhost:8080/api/admin/seed-matchups/status
```

- **Re-seeding is safe.** Existing rows are updated with fresh numbers and new ones are inserted, so run it again after a Dota patch.
- **Matchup seeding makes one OpenDota request per hero**, about 1 second apart to stay under the free tier's 60 requests/minute. Rate-limit responses (429) are retried with backoff. If some heroes still fail, the job ends as `Failed` and lists them in `failedHeroIds`. Re-run it later to fill them in.
- **Job status is kept in memory.** It resets when the API restarts, and only one run can be queued or running at a time (a second request gets `409 Conflict`).

## API

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/heroes` | none | All heroes, sorted by name |
| `GET` | `/api/heroes/{id}/counters?limit=5` | none | Top counter-picks against a hero (`limit` 1–20) |
| `POST` | `/api/admin/seed-heroes` | `X-Admin-Key` | Fetch and upsert heroes from OpenDota |
| `POST` | `/api/admin/seed-matchups` | `X-Admin-Key` | Queue a matchup seeding job; returns `202` with a `Location` header |
| `GET` | `/api/admin/seed-matchups/status` | `X-Admin-Key` | State of the latest seeding job |

Example requests are in [`DotaDraft.Api/DotaDraft.Api.http`](DotaDraft.Api/DotaDraft.Api.http). It reads the admin key from the `DOTADRAFT_ADMIN_KEY` environment variable.

Admin endpoints return `401` without a valid key. If `Admin:ApiKey` isn't configured they aren't mapped at all and return `404`, so a missing setting never leaves them open.

### How counters are ranked

A counter in the response looks like this (shortened):

```json
{ "id": 63, "localizedName": "Weaver", "advantage": 12.4, "winRate": 73.91, "baselineWinRate": 45.34, "gamesPlayed": 23 }
```

The list is sorted by `advantage`: how many percentage points better the counter hero does against this opponent than it does in general, scaled down when there are few games to go on:

```
raw edge  = winRate − baselineWinRate
trust     = gamesPlayed ÷ (gamesPlayed + k)
advantage = raw edge × trust
```

- **`baselineWinRate`** is the hero's win rate across all its matchups. Subtracting it stops heroes that are simply strong from looking like counters to everyone.
- **`trust`** stops lucky small samples (say 5 wins in 6 games) from topping the list. With `k = 30`, a matchup with 30 games keeps half its raw edge, and one with 300 games keeps about 90%.
- **`k`** is the `Counters:PriorGames` setting (default 30). Raise it to trust small samples less.

OpenDota's matchup data appears to come from high-level/pro matches, so samples are small (a median of about 45 games per matchup).

## Configuration

Settings come from `appsettings.json`, then `appsettings.Development.json`, then user-secrets (Development only), then environment variables. In production, set secrets as environment variables, using `__` in place of `:` (for example `Admin__ApiKey`).

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | none (required) | PostgreSQL connection string |
| `Admin:ApiKey` | none | Key for the admin endpoints; they are disabled when unset |
| `OpenDota:ApiKey` | none | Optional OpenDota API key, sent as a bearer token |
| `OpenDota:MatchupRequestDelayMs` | `1100` | Delay between per-hero matchup requests |
| `OpenDota:MaxRetryAttempts` | `3` | Retries after a transient failure (429, 5xx, network) |
| `OpenDota:RetryBaseDelayMs` | `5000` | First retry delay; doubles on each retry |
| `OpenDota:RetryMaxDelayMs` | `60000` | Longest retry delay; a longer `Retry-After` is not retried |
| `Counters:PriorGames` | `30` | `k` in the counter ranking |
| `Cors:AllowedOrigins` | `http://localhost:4200` in Development, none otherwise | Origins allowed to call the API from a browser |

**CORS:** in Development, the Angular dev server (`ng serve` on port 4200) can call the API directly. In production, no origins are allowed until you configure them, for example `Cors__AllowedOrigins__0=https://your-app.example.com`. Serve the API over HTTPS there, since the admin key travels in a header.

## Tests

```bash
dotnet test                                      # API
cd DotaDraft.Web && npm test -- --watch=false    # frontend (Vitest)
```

The API tests don't need Docker, the database or any secrets. They use EF Core's in-memory database and a fake OpenDota client. HTTP-level tests start the real app in memory through `DotaDraftApiFactory`. The frontend tests stub HTTP calls with Angular's `HttpTestingController`.

## Project structure

```
DotaDraft.Api/
  Endpoints/               Minimal API endpoints and the admin key filter
  Services/                Hero/matchup ingestion, the seeding background job, counter ranking
  Integrations/OpenDota/   OpenDota HTTP client (with retries), its DTOs and options
  Dtos/                    This API's response contracts
  Models/, Data/           EF Core entities and DbContext
  Migrations/              EF Core migrations
DotaDraft.Api.Tests/       xUnit tests
DotaDraft.Web/             Angular client (Tailwind CSS, Vitest)
  src/app/heroes/          API models and HeroApiService
  src/environments/        API base URL per build configuration
```
