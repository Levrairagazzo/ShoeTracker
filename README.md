

# ShoeTracker

ShoeTracker is a small web app for runners to track mileage on their running shoes. You log your shoes and the runs you do in them, and the app tells you when a shoe has passed its retirement threshold.

## Features

- **Add shoes** — record a name, brand, purchase date, and a retirement threshold (in km, defaults to 700 km).
- **Log runs** — attach a date and distance (km) to a specific shoe.
- **Shoe list / dashboard** — see every shoe with its total accumulated distance and a status of `OK` or `Retire me!` once it crosses its threshold.
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

- **Client** (`client/`): a Vite + React + TypeScript SPA. `App.tsx` loads the shoe list on mount and renders three pieces: `ShoeList`, `AddShoeForm`, and `LogRunForm`. All server communication goes through a small typed fetch wrapper, `api/shoeTrackerClient.ts`, which calls a relative `/api` base path and surfaces validation errors (`ApiError` wraps the API's `ProblemDetails` response).
- **API** (`src/ShoeTracker.Api/`): an ASP.NET Core minimal API (no MVC controllers). Endpoints are grouped by resource in `Endpoints/ShoeEndpoints.cs` and `Endpoints/RunEndpoints.cs`, registered via extension methods (`MapShoeEndpoints`, `MapRunEndpoints`) in `Program.cs`. Request/response shapes are plain C# records in `Dtos/`. Domain entities (`Models/Shoe.cs`, `Models/Run.cs`) are persisted via EF Core (`Data/ShoeTrackerContext.cs`), with a one-to-many relationship between a shoe and its runs. Mileage math (total distance, over-threshold check) lives in `Services/MileageCalculator.cs`, kept separate from the endpoint handlers so it's independently unit-testable.
- **Database**: SQLite, accessed through EF Core migrations (`Migrations/`). On startup, `Program.cs` runs `db.Database.Migrate()` so the schema is always up to date. In Docker, the SQLite file lives on a named volume (`shoe-data`) so data survives container restarts.
- **Tests** (`tests/ShoeTracker.Api.Tests/`): xUnit tests for the mileage calculation logic.
- **Reverse proxy**: in the Docker setup, nginx (`client/nginx.conf`) serves the built static SPA and proxies any `/api/*` request to the API container, so the browser only ever talks to one origin.

### API endpoints

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/shoes` | List all shoes with computed total distance and retirement status |
| `GET` | `/shoes/{id}` | Get a single shoe |
| `POST` | `/shoes` | Create a shoe |
| `POST` | `/shoes/{shoeId}/runs` | Log a run against a shoe |

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
