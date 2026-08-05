

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
- **API** (`src/ShoeTracker.Api/`): an ASP.NET Core minimal API (no MVC controllers). Endpoints are grouped by resource in `Endpoints/ShoeEndpoints.cs` and `Endpoints/RunEndpoints.cs`, registered via extension methods (`MapShoeEndpoints`, `MapRunEndpoints`) in `Program.cs`. Request/response shapes are plain C# records in `Dtos/`. Domain entities (`Models/Shoe.cs`, `Models/Run.cs`) are persisted via EF Core (`Data/ShoeTrackerContext.cs`), with a one-to-many relationship between a shoe and its runs. Mileage math (total distance, over-threshold check) lives in `Services/MileageCalculator.cs`, kept separate from the endpoint handlers so it's independently unit-testable.
- **Database**: SQLite, accessed through EF Core migrations (`Migrations/`). On startup, `Program.cs` runs `db.Database.Migrate()` so the schema is always up to date. In Docker, the SQLite file lives on a named volume (`shoe-data`) so data survives container restarts.
- **Tests** (`tests/ShoeTracker.Api.Tests/`): xUnit tests for the mileage calculation logic.
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
docker compose up --build
```

The client will be available at `http://localhost:8080`, proxying API calls to the API service.

> Note: `docker-compose.yml` also includes a `caddy` service for production HTTPS (see "Deploying" below). Caddy can't obtain a certificate for `milesleft.run` from `localhost`, so for local dev run `docker compose up --build api client` instead and hit the client container directly — or add back a `ports: - "8080:80"` mapping on `client` for the session.

### Manually

**API**
```bash
cd src/ShoeTracker.Api
dotnet run
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

The app has no user accounts — it's designed for a single person to run behind HTTP Basic Auth. Before deploying anywhere reachable from the internet:

**1. Set a username and password.**

```bash
mkdir -p secrets
htpasswd -c -B secrets/.htpasswd <your-username>
```

This creates `secrets/.htpasswd` (bcrypt-hashed, `.gitignore`d — never commit it) and `docker-compose.yml` mounts it into the `client` container at `/etc/nginx/.htpasswd`. nginx (`client/nginx.conf`) gates the whole app — both the static site and the `/api/*` proxy — behind it via `auth_basic`. If the file isn't present, the container refuses to start rather than serving unauthenticated.

To add or change a user later: `htpasswd -B secrets/.htpasswd <username>` (drop `-c`, which would overwrite the file).

**2. Put it behind HTTPS.**

nginx here only speaks plain HTTP — it's meant to sit behind something that terminates TLS for you. This repo ships a `caddy` service (`docker-compose.yml`) and a root-level `Caddyfile` that does exactly that: point a domain's DNS A record at your host, edit the `Caddyfile` to use your domain, and Caddy automatically obtains and renews a Let's Encrypt certificate for it, terminating HTTPS and proxying to the `client` service. Basic Auth credentials are only base64-encoded, not encrypted, so the app should never be exposed over plain HTTP once it's off `localhost` — Caddy handles that here, and `client` intentionally publishes no host port of its own so Caddy is the only entry point.

**3. Set `AllowedHosts` once you have a real domain.**

By default the API answers to any hostname (`AllowedHosts: "*"` in `appsettings.json`). Once you know the domain the app will actually live at, set it via an `ALLOWED_HOSTS` environment variable — copy `.env.example` to `.env` next to `docker-compose.yml` (gitignored, `docker compose` picks it up automatically) and fill in your domain:

```
ALLOWED_HOSTS=yourdomain.example.com
```

Leaving it unset keeps today's behavior (`*`), so this is safe to skip until you've picked a host.

**4. Production logging.**

`appsettings.Production.json` quiets the logs down for a live deployment (it's layered on top of `appsettings.json` automatically whenever `ASPNETCORE_ENVIRONMENT=Production`, which is the default unless overridden) — nothing to configure here, just noting it exists.

### Backing up the database

All data lives in one SQLite file inside the `shoe-data` Docker volume — nothing backs it up automatically. To take a manual snapshot while the app is running:

```bash
./scripts/backup-db.sh
```

This writes a timestamped copy to `./backups/` (gitignored — these are your real running logs, never commit them). It's safe to run at any time; it takes a consistent snapshot via SQLite's own backup mechanism rather than copying the file directly, so it won't produce a corrupt copy even if the app is actively writing to it.
