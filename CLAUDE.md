# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ShoeTracker is a small web app for runners to track mileage on their running shoes: log shoes and the runs done in them, and the app computes when a shoe has passed its retirement threshold (default 700 km) server-side from its logged runs (not stored redundantly).

## Architecture

Two-tier app: a React SPA (`client/`) talks over `/api/*` to an ASP.NET Core minimal API (`src/ShoeTracker.Api/`), backed by SQLite via EF Core. In Docker, Caddy terminates HTTPS in front of nginx, which serves the built client and reverse-proxies `/api/*` to the API container so the browser only talks to one origin.

**API** (`src/ShoeTracker.Api/`):
- Minimal API, no MVC controllers. Endpoints are grouped by resource in `Endpoints/ShoeEndpoints.cs`, `Endpoints/RunEndpoints.cs` and `Endpoints/AuthEndpoints.cs`, registered via extension methods (`MapShoeEndpoints`, `MapRunEndpoints`, `MapAuthEndpoints`) called from `Program.cs`.
- Request/response shapes are plain C# records in `Dtos/`.
- Domain entities (`Models/Shoe.cs`, `Models/Run.cs`) are persisted via EF Core (`Data/ShoeTrackerContext.cs`); one shoe has many runs.
- Mileage math (total distance, over-threshold check) lives in `Services/MileageCalculator.cs`, deliberately kept out of endpoint handlers so it's independently unit-testable — this is the pattern to follow for any new business logic.
- Validation (required fields, positive distance/threshold, no future-dated runs) happens inline in the endpoint handlers via `Results.ValidationProblem`, not via data annotations or a separate validation layer.
- On startup, `Program.cs` runs `db.Database.Migrate()`, so the schema is always brought up to date automatically — no manual migration step needed when running the API.
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
| `POST` | `/auth/login` | Log in with email/password, establishes a cookie session |
| `POST` | `/auth/logout` | Log out, clears the session (requires an active session) |
| `GET` | `/auth/me` | Get the current logged-in user (requires an active session) |

Note: `/shoes` and `/shoes/{shoeId}/runs` endpoints require an active app-level login session (`.RequireAuthorization()`) but are not yet scoped per-user (see "Deployment & auth" below) — that scoping is planned for a later step.

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

**Run tests** (from `tests/ShoeTracker.Api.Tests/`):
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

**Back up the live database** (from repo root, requires the app running via `docker compose`):
```bash
./scripts/backup-db.sh
```

**EF Core migrations** (from `src/ShoeTracker.Api/`) — `dotnet-ef` is a local tool pinned in `dotnet-tools.json`; run `dotnet tool restore` once if it's not on PATH:
```bash
dotnet ef migrations add <Name>
dotnet ef database update
```

## Deployment & auth

- The app has a single application-level user account (email + PBKDF2-hashed password, in a `Users` table via `Services/PasswordHasher.cs`) — there's no self-serve sign-up. The one account is created by a startup seed step in `Program.cs`: if no `User` row exists yet, it reads `SeedAdminEmail`/`SeedAdminPassword` from configuration and creates the account (idempotent — re-runs are a no-op once the account exists, so leaving the env vars set permanently is safe). `POST /auth/login` establishes an ASP.NET Core cookie-based session (14-day sliding expiration); `GET /auth/me` / `POST /auth/logout` round out the session lifecycle. This app-level login is a separate, *inner* layer. `/shoes` and `/shoes/{shoeId}/runs` endpoints require an active login session (`.RequireAuthorization()`) but are not yet scoped to the specific logged-in user's data (planned for a later step).
- For local dev, set the seed credentials via `dotnet user-secrets set "SeedAdminEmail" "..."` / `"SeedAdminPassword" "..."` from `src/ShoeTracker.Api/` (or shell env vars) — never put real credentials in `appsettings.Development.json`, which is committed to git.
- The static SPA has no outer HTTP Basic Auth gate — `client/nginx.conf` serves it directly. The app-level cookie session described above is the only auth layer; nginx just serves the built client and reverse-proxies `/api/*` to the API container.
- TLS is terminated by the `caddy` service in `docker-compose.yml`, configured by the root `Caddyfile` (domain `milesleft.run`, HSTS header, automatic Let's Encrypt certificates, reverse-proxying to `client:80`). nginx only ever speaks plain HTTP. Caddy is the only service publishing host ports (80/443); `client` publishes none, so Caddy is the sole entry point in production.
- For local dev, copy `docker-compose.override.yml.example` to `docker-compose.override.yml` (gitignored, auto-merged by `docker compose up`), which publishes `client` on `localhost:8080`. Caddy can't get a certificate from localhost; run `docker compose up --build api client` to skip it entirely.
- `AllowedHosts` is wired as an environment variable override in `docker-compose.yml` (`ALLOWED_HOSTS`, defaulting to `*`) so the real deploy domain can be set via a `.env` file without a code change.
- `appsettings.Production.json` quiets logging for `ASPNETCORE_ENVIRONMENT=Production`; note `dotnet run` applies `Properties/launchSettings.json`'s `ASPNETCORE_ENVIRONMENT=Development` regardless of shell env vars unless you pass `--no-launch-profile`.
- `scripts/backup-db.sh` takes a manual point-in-time snapshot of the live SQLite volume via `sqlite3 .backup` (not a raw file copy — WAL-mode SQLite needs a real backup call, and the volume must be mounted read-write, not read-only, for that to work). No automated/scheduled backup exists yet.

## Branches

`prod` is the default branch (renamed from `main`) and tracks what's deployed. `dev` is for active feature work. When starting new feature work, branch from `dev`, not `prod`.

## Tech stack

- Backend: .NET 10 / ASP.NET Core Minimal APIs, EF Core 10 with SQLite, xUnit
- Frontend: React 19 + TypeScript, Vite 8, oxlint, Tailwind CSS v4
- Infra: Docker (separate Dockerfiles for API and client), nginx reverse proxy, Caddy for HTTPS, Docker Compose with a named volume for the SQLite database, GitHub Actions CI (`.github/workflows/ci.yml`: API build/test, client lint/build)