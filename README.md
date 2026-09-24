

# ShoeTracker

ShoeTracker is a small web app for runners to track mileage on their running shoes. You log your shoes and the runs you do in them, and the app tells you when a shoe has passed its retirement threshold.

## Features

- **Add, edit, and delete shoes** — record a name, brand, purchase date, and a retirement threshold (in km, defaults to 700 km); deleting a shoe cascades to its runs.
- **Log, edit, and delete runs** — attach a date and distance (km) to a specific shoe, with a per-shoe run history view.
- **Shoe grid / dashboard** — every shoe as an expandable card showing its total accumulated distance and a status of `OK` or `Retire me!` once it crosses its threshold; expanding a card lazily loads its run history.
- **Automatic mileage totals** — total distance and retirement status are computed server-side from the shoe's logged runs, not stored redundantly.
- **Input validation** — required fields, positive distances/thresholds, and a rule preventing runs from being logged with a future date.

## Architecture

The app is a classic two-tier web application: a single-page React frontend that talks to a REST API backed by a SQL database.

```
┌─────────────────┐        HTTP/JSON        ┌─────────────────────┐        EF Core        ┌──────────────┐
│  client (React) │ ───────────────────────▶│  ShoeTracker.Api    │ ──────────────────────▶│  SQLite DB   │
│  served by nginx│  /api/* (reverse proxy) │  (ASP.NET Core       │                        │  (file-based)│
└─────────────────┘                          │  minimal API)        │                        └──────────────┘
                                              └─────────────────────┘
```

- **Client** (`client/`): a Vite + React + TypeScript SPA. `App.tsx` loads the shoe list on mount, renders a header with "Log Run"/"Add Shoe" actions that open `Modal`-wrapped forms, and renders `ShoeGrid`. `ShoeGrid` renders a `ShoeCard` per shoe (an expand/collapse card with edit/delete actions and a lazily-loaded `RunHistoryList`); `ShoeForm` is dual-purpose for both creating and editing a shoe. All server communication goes through a small typed fetch wrapper, `api/shoeTrackerClient.ts`, which calls a relative `/api` base path and surfaces validation errors (`ApiError` wraps the API's `ProblemDetails` response).
- **API** (`src/ShoeTracker.Api/`): an ASP.NET Core minimal API (no MVC controllers). Endpoints are grouped by resource in `Endpoints/ShoeEndpoints.cs`, `Endpoints/RunEndpoints.cs`, and `Endpoints/AuthEndpoints.cs`, registered via extension methods (`MapShoeEndpoints`, `MapRunEndpoints`, `MapAuthEndpoints`) in `Program.cs`. Request/response shapes are plain C# records in `Dtos/`. Domain entities (`Models/Shoe.cs`, `Models/Run.cs`, `Models/User.cs`) are persisted via EF Core (`Data/ShoeTrackerContext.cs`), with a one-to-many relationship between a shoe and its runs. Mileage math (total distance, over-threshold check) lives in `Services/MileageCalculator.cs`, kept separate from the endpoint handlers so it's independently unit-testable. App-level login uses cookie-based session auth, with password hashing hand-rolled in `Services/PasswordHasher.cs` (PBKDF2, no Identity framework).
- **Database**: SQLite, accessed through EF Core migrations (`Migrations/`). On startup, `Program.cs` runs `db.Database.Migrate()` so the schema is always up to date. In Docker, the SQLite file lives on a named volume (`shoe-data`) so data survives container restarts.
- **Tests** (`tests/ShoeTracker.Api.Tests/`): xUnit tests for the mileage calculation logic and password hashing.
- **Reverse proxy**: in the Docker setup, nginx (`client/nginx.conf`) serves the built static SPA and proxies any `/api/*` request to the API container, so the browser only ever talks to one origin.
- **CI**: GitHub Actions (`.github/workflows/ci.yml`) runs `dotnet test` and `npm run lint && npm run build` on every push and pull request. The `prod` branch requires both checks to pass before a PR can merge.

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
| `POST` | `/auth/login` | Log in, establishing a cookie session |
| `POST` | `/auth/logout` | Log out and clear the session |
| `GET` | `/auth/me` | Get the current logged-in user |

## Tech stack

**Backend**
- .NET 10 / ASP.NET Core Minimal APIs
- Entity Framework Core 10 with the SQLite provider
- xUnit for testing

**Frontend**
- React 19 + TypeScript
- Vite 8 as the build tool/dev server
- oxlint for linting
- Tailwind CSS v4 (via `@tailwindcss/vite`) for styling

**Infrastructure**
- Docker (separate `Dockerfile`s for the API and client)
- nginx, serving the built client and reverse-proxying `/api` to the API container
- Docker Compose to run both services together, with a named volume for the SQLite database

## Running locally

### With Docker Compose (recommended)

```bash
cp docker-compose.override.yml.example docker-compose.override.yml  # first time only
docker compose up --build
```

The client will be available at `http://localhost:8080`.

> Why the override file: `docker-compose.yml` includes a `caddy` service for production HTTPS (see "Deploying" below), and in production `caddy` is the only service that publishes a host port — `client` publishes none, so Caddy is the sole entry point. Caddy can't obtain a real certificate for `milesleft.run` from `localhost` (no public DNS points here), so without the override nothing is reachable at `localhost:8080`. `docker-compose.override.yml` (gitignored, auto-merged by `docker compose up`, never deployed) adds a `client` port mapping back for local dev only. If you'd rather skip Caddy's cert-retry noise in the logs entirely, run `docker compose up --build api client` instead — the override still applies.

### Manually

**API**
```bash
cd src/ShoeTracker.Api
dotnet run
```

The app has one login account, created on first boot from `SeedAdminEmail`/`SeedAdminPassword` config. For local dev, set these without committing real credentials:
```bash
cd src/ShoeTracker.Api
dotnet user-secrets set "SeedAdminEmail" "you@example.com"
dotnet user-secrets set "SeedAdminPassword" "change-me"
```

**Client**
```bash
cd client
npm install
npm run dev
```

### Tests

```bash
cd tests/ShoeTracker.Api.Tests
dotnet test
```

## Deploying

The app has a single app-level login account (see "Running locally" above) — it's designed for a single person to run. Before deploying anywhere reachable from the internet:

**1. Put it behind HTTPS.**

nginx here only speaks plain HTTP — it's meant to sit behind something that terminates TLS for you. This repo ships a `caddy` service (`docker-compose.yml`) and a root-level `Caddyfile` that does exactly that: point a domain's DNS A record at your host, edit the `Caddyfile` to use your domain, and Caddy automatically obtains and renews a Let's Encrypt certificate for it, terminating HTTPS and proxying to the `client` service. The app-level login's session cookie should never travel over plain HTTP once it's off `localhost` — Caddy handles that here, and `client` intentionally publishes no host port of its own so Caddy is the only entry point.

**2. Set `AllowedHosts` and your login credentials.**

By default the API answers to any hostname (`AllowedHosts: "*"` in `appsettings.json`). Once you know the domain the app will actually live at, set it via an `ALLOWED_HOSTS` environment variable — copy `.env.example` to `.env` next to `docker-compose.yml` (gitignored, `docker compose` picks it up automatically) and fill in your domain, plus `SEED_ADMIN_EMAIL`/`SEED_ADMIN_PASSWORD` for the app-level login account (only used to create the account on first boot — see "Running locally" above):

```
ALLOWED_HOSTS=yourdomain.example.com
SEED_ADMIN_EMAIL=you@example.com
SEED_ADMIN_PASSWORD=change-me
```

Leaving `ALLOWED_HOSTS` unset keeps today's behavior (`*`), so it's safe to skip until you've picked a host. Leaving the seed vars unset skips seeding — login stays unavailable until you set them and restart.

**4. Production logging.**

`appsettings.Production.json` quiets the logs down for a live deployment (it's layered on top of `appsettings.json` automatically whenever `ASPNETCORE_ENVIRONMENT=Production`, which is the default unless overridden) — nothing to configure here, just noting it exists.

### Backing up the database

All data lives in one SQLite file inside the `shoe-data` Docker volume. The `backup` service in `docker-compose.yml` snapshots it automatically: once when it starts, then every `BACKUP_INTERVAL_HOURS` (default 24). It keeps the newest `BACKUP_KEEP` (default 14) automated snapshots and deletes older ones. Both are set in `.env`. Snapshots land in `./backups/` on the host as `shoetracker-auto-<UTC timestamp>.db` (gitignored — this is your real running log, never commit it).

Each snapshot uses SQLite's own backup mechanism rather than a file copy, so it's safe while the app is writing, and it's integrity-checked before it's kept. Watch it with `docker compose logs backup`.

To take a one-off snapshot (e.g. right before a deploy that runs migrations):

```bash
./scripts/backup-db.sh
```

Manual snapshots are named `shoetracker-<UTC timestamp>.db` and are **never** deleted by the automated retention.

> These backups live on the same host as the database, so they protect against bad writes, bad migrations and accidental deletes — not against losing the host itself. Copy `./backups/` somewhere off the machine periodically if that matters to you.

### Restoring the database

```bash
./scripts/restore-db.sh backups/shoetracker-auto-20260925-030000.db
```

This integrity-checks the backup, asks you to confirm (pass `--yes` to skip), takes a manual safety snapshot of the current database so the restore can itself be undone, stops the `api` and `backup` services, swaps the file into the volume, and starts them again. Anything written after the chosen backup was taken is lost (apart from the safety snapshot).
