# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ShoeTracker is a small web app for runners to track mileage on their running shoes: log shoes and the runs done in them, and the app computes when a shoe has passed its retirement threshold (default 700 km) server-side from its logged runs (not stored redundantly).

## Architecture

Two-tier app: a React SPA (`client/`) talks over `/api/*` to an ASP.NET Core minimal API (`src/ShoeTracker.Api/`), backed by SQLite via EF Core. In Docker, Caddy terminates HTTPS in front of nginx, which serves the built client and reverse-proxies `/api/*` to the API container so the browser only talks to one origin.

**API** (`src/ShoeTracker.Api/`):
- Minimal API, no MVC controllers. Endpoints are grouped by resource in `Endpoints/ShoeEndpoints.cs`, `Endpoints/RunEndpoints.cs` and `Endpoints/AuthEndpoints.cs`, registered via extension methods (`MapShoeEndpoints`, `MapRunEndpoints`, `MapAuthEndpoints`) called from `Program.cs`.
- Request/response shapes are plain C# records in `Dtos/`.
- Domain entities (`Models/User.cs`, `Models/Shoe.cs`, `Models/Run.cs`) are persisted via EF Core (`Data/ShoeTrackerContext.cs`); one user has many shoes and many runs, and one shoe has many runs (all cascade on delete). A run's `ShoeId` is nullable: a run can be *unassigned* (e.g. imported from Strava before it's attributed to a shoe), so every run carries its own `UserId` and ownership is checked on `Run.UserId`, never derived through `Run.Shoe`. `Run.Source` is `Manual` or `Strava` (stored as a string), with `StravaActivityId` unique per user.
- Mileage math (total distance, over-threshold check) lives in `Services/MileageCalculator.cs`, deliberately kept out of endpoint handlers so it's independently unit-testable — this is the pattern to follow for any new business logic.
- Validation (required fields, positive distance/threshold, no future-dated runs) happens inline in the endpoint handlers via `Results.ValidationProblem`, not via data annotations or a separate validation layer.
- On startup, `Program.cs` migrates the database automatically — no manual migration step needed when running the API. Migration is staged around the admin seed step: a database that hasn't yet applied `AddShoeOwnership` is migrated only to `AddUsers`, the admin account is seeded, then the remaining migrations run. This is because `AddShoeOwnership` backfills pre-existing shoes to the lowest-Id user, so that user must exist first.
- Connection string / DB file path is configured in `appsettings.json` (`ConnectionStrings:ShoeTrackerContext`, SQLite file `shoetracker.db`). In Docker the SQLite file lives on a named volume (`shoe-data`) so data survives restarts.

**Client** (`client/src/`):
- Vite + React 19 + TypeScript SPA, styled with Tailwind CSS v4 (via `@tailwindcss/vite`, CSS-first config — design tokens are CSS custom properties in `index.css`, exposed to Tailwind via a `@theme inline` block so both dark mode and Tailwind utility classes read from the same source).
- `App.tsx` loads the shoe list on mount, renders a header with "Add Shoe"/"Log Run" actions that open `Modal`-wrapped forms, and renders `ShoeGrid`.
- `components/`: `ShoeGrid` (responsive card grid + empty state) renders `ShoeCard` per shoe. `ShoeCard` is a `<details>/<summary>` expand/collapse card showing the key stat + status pill always, with edit/delete-shoe actions and a lazily-loaded `RunHistoryList` (per-run inline edit/delete) in the expanded state. `ShoeForm` is dual-purpose — pass a `shoe` prop to edit, omit it to create. `Modal` wraps the native `<dialog>` element (used for add/edit shoe). `icons.tsx` holds small inline SVG icon components — no icon library dependency.
- All server communication goes through `api/shoeTrackerClient.ts`, a small typed fetch wrapper that calls a relative `/api` base path and surfaces validation errors via `ApiError`, which wraps the API's `ProblemDetails` response. Add new API calls here rather than calling `fetch` directly from components.

### API endpoints

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/shoes` | List all shoes with computed total distance and retirement status |
| `GET` | `/shoes/{id}` | Get a single shoe |
| `POST` | `/shoes` | Create a shoe |
| `PUT` | `/shoes/{id}` | Update a shoe |
| `DELETE` | `/shoes/{id}` | Delete a shoe (cascades to its runs) |
| `GET` | `/shoes/{shoeId}/runs` | List a shoe's runs, most recent first |
| `POST` | `/shoes/{shoeId}/runs` | Log a run against a shoe |
| `PUT` | `/shoes/{shoeId}/runs/{id}` | Update a run |
| `DELETE` | `/shoes/{shoeId}/runs/{id}` | Delete a run |
| `GET` | `/runs` | List all of the current user's runs, most recent first, including runs not assigned to a shoe (`?unassigned=true` returns only those) |
| `POST` | `/auth/login` | Log in with email/password, establishes a cookie session |
| `POST` | `/auth/logout` | Log out, clears the session (requires an active session) |
| `GET` | `/auth/me` | Get the current logged-in user (requires an active session) |

Note: `/shoes`, `/shoes/{shoeId}/runs` and `/runs` endpoints require an active app-level login session (`.RequireAuthorization()`) and are scoped to the logged-in user: every query filters on the current user's id (`ClaimsPrincipal.GetUserId()` in `Endpoints/ClaimsPrincipalExtensions.cs`, read from the cookie's `NameIdentifier` claim). Another user's shoe or run returns `404`, not `403`, so its existence isn't revealed. Any new endpoint touching shoes/runs must apply the same filter — `UserScopingTests` covers every existing endpoint.

Validation logic shared between create/update is factored into a private `Validate...` helper in each endpoint file — follow that pattern rather than duplicating the checks inline when adding new mutating endpoints.

## Commands

**Run the API** (from `src/ShoeTracker.Api/`):
```bash
dotnet run
```

**Run the client dev server** (from `client/`):
```bash
npm install   # first time only
npm run dev
```

**Run everything via Docker Compose** (from repo root) — client at `http://localhost:8080` (requires the local `docker-compose.override.yml`, see "Deployment & auth"), proxying to the API:
```bash
docker compose up --build
```

**Run tests** (from `tests/ShoeTracker.Api.Tests/`) — unit tests for services, plus `WebApplicationFactory`-based integration tests (`AuthEndpointsTests`, `ShoeEndpointsTests`, `RunEndpointsTests`, `UserScopingTests`) that run the real API against a throwaway SQLite file, and `MigrationTests` for migrations that transform existing data. New integration tests should derive from `ApiTestBase`, which sets up the app with two accounts (`OwnerEmail`, `OtherEmail`) and provides login/create-shoe/log-run helpers:
```bash
dotnet test
```
Run a single test:
```bash
dotnet test --filter "FullyQualifiedName~MileageCalculatorTests.IsOverThreshold_WhenTotalExceedsThreshold_ReturnsTrue"
```

**Client build / lint** (from `client/`):
```bash
npm run build     # tsc -b && vite build
npm run lint       # oxlint
```

**Back up / restore the live database** (from repo root, via `docker compose`):
```bash
./scripts/backup-db.sh                          # one-off manual snapshot, never pruned
./scripts/restore-db.sh backups/<file>.db       # restore (takes a safety snapshot first)
```

**EF Core migrations** (from `src/ShoeTracker.Api/`) — `dotnet-ef` is a local tool pinned in `dotnet-tools.json`; run `dotnet tool restore` once if it's not on PATH:
```bash
dotnet ef migrations add <Name>
dotnet ef database update
```

## Deployment & auth

- The app has a single application-level user account (email + PBKDF2-hashed password, in a `Users` table via `Services/PasswordHasher.cs`) — there's no self-serve sign-up. The one account is created by a startup seed step in `Program.cs`: if no `User` row exists yet, it reads `SeedAdminEmail`/`SeedAdminPassword` from configuration and creates the account (idempotent — re-runs are a no-op once the account exists, so leaving the env vars set permanently is safe). `POST /auth/login` establishes an ASP.NET Core cookie-based session (14-day sliding expiration); `GET /auth/me` / `POST /auth/logout` round out the session lifecycle. `/shoes`, `/shoes/{shoeId}/runs` and `/runs` endpoints require an active login session and only ever expose the logged-in user's own data (see the note under "API endpoints"). Additional accounts can only be added directly in the `Users` table for now.
- For local dev, set the seed credentials via `dotnet user-secrets set "SeedAdminEmail" "..."` / `"SeedAdminPassword" "..."` from `src/ShoeTracker.Api/` (or shell env vars) — never put real credentials in `appsettings.Development.json`, which is committed to git.
- The static SPA has no outer HTTP Basic Auth gate — `client/nginx.conf` serves it directly. The app-level cookie session described above is the only auth layer; nginx just serves the built client and reverse-proxies `/api/*` to the API container.
- TLS is terminated by the `caddy` service in `docker-compose.yml`, configured by the root `Caddyfile` (domain `milesleft.run`, HSTS header, automatic Let's Encrypt certificates, reverse-proxying to `client:80`). nginx only ever speaks plain HTTP. Caddy is the only service publishing host ports (80/443); `client` publishes none, so Caddy is the sole entry point in production.
- For local dev, copy `docker-compose.override.yml.example` to `docker-compose.override.yml` (gitignored, auto-merged by `docker compose up`), which publishes `client` on `localhost:8080`. Caddy can't get a certificate from localhost; run `docker compose up --build api client` to skip it entirely.
- `AllowedHosts` is wired as an environment variable override in `docker-compose.yml` (`ALLOWED_HOSTS`, defaulting to `*`) so the real deploy domain can be set via a `.env` file without a code change.
- `appsettings.Production.json` quiets logging for `ASPNETCORE_ENVIRONMENT=Production`; note `dotnet run` applies `Properties/launchSettings.json`'s `ASPNETCORE_ENVIRONMENT=Development` regardless of shell env vars unless you pass `--no-launch-profile`.
- Backups: the `backup` service (`backup/Dockerfile` + `backup/backup.sh`, Alpine + sqlite3) snapshots the live DB into `./backups` on start and every `BACKUP_INTERVAL_HOURS` (default 24), integrity-checks each snapshot, and keeps the newest `BACKUP_KEEP` (default 14) `shoetracker-auto-*.db` files. `scripts/backup-db.sh` runs the same image in `once` mode to write a manual `shoetracker-*.db` snapshot, which retention never prunes. `scripts/restore-db.sh` integrity-checks a backup, takes a safety snapshot, stops `api`/`backup`, swaps the file into the volume (deleting the old `-wal`/`-shm`), and restarts them. Two things matter here. First, snapshots use `sqlite3 .backup`, not a file copy, and the volume must be mounted read-write because WAL-mode SQLite has to touch its sidecar files even to read. Second, `backup.sh` runs sqlite3 as the DB file's owner (the API's non-root `app` user, uid 1654) via `su-exec`; running as root could leave root-owned `-wal`/`-shm` files that the API can't write. Backups are same-host only, with no off-site copy.

## Branches

`prod` is the default branch (renamed from `main`) and tracks what's deployed. `dev` is for active feature work. When starting new feature work, branch from `dev`, not `prod`.

## Tech stack

- Backend: .NET 10 / ASP.NET Core Minimal APIs, EF Core 10 with SQLite, xUnit
- Frontend: React 19 + TypeScript, Vite 8, oxlint, Tailwind CSS v4
- Infra: Docker (separate Dockerfiles for API and client), nginx reverse proxy, Caddy for HTTPS, Docker Compose with a named volume for the SQLite database, GitHub Actions CI (`.github/workflows/ci.yml`: API build/test, client lint/build)