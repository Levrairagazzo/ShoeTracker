# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ShoeTracker is a small web app for runners to track mileage on their running shoes: log shoes and the runs done in them, and the app computes when a shoe has passed its retirement threshold (default 700 km) server-side from its logged runs (not stored redundantly).

## Architecture

Two-tier app: a React SPA (`client/`) talks over `/api/*` to an ASP.NET Core minimal API (`src/ShoeTracker.Api/`), backed by SQLite via EF Core. In Docker, nginx serves the built client and reverse-proxies `/api/*` to the API container so the browser only talks to one origin.

**API** (`src/ShoeTracker.Api/`):
- Minimal API, no MVC controllers. Endpoints are grouped by resource in `Endpoints/ShoeEndpoints.cs` and `Endpoints/RunEndpoints.cs`, registered via extension methods (`MapShoeEndpoints`, `MapRunEndpoints`) called from `Program.cs`.
- Request/response shapes are plain C# records in `Dtos/`.
- Domain entities (`Models/Shoe.cs`, `Models/Run.cs`) are persisted via EF Core (`Data/ShoeTrackerContext.cs`); one shoe has many runs.
- Mileage math (total distance, over-threshold check) lives in `Services/MileageCalculator.cs`, deliberately kept out of endpoint handlers so it's independently unit-testable — this is the pattern to follow for any new business logic.
- Validation (required fields, positive distance/threshold, no future-dated runs) happens inline in the endpoint handlers via `Results.ValidationProblem`, not via data annotations or a separate validation layer.
- On startup, `Program.cs` runs `db.Database.Migrate()`, so the schema is always brought up to date automatically — no manual migration step needed when running the API.
- Connection string / DB file path is configured in `appsettings.json` (`ConnectionStrings:ShoeTrackerContext`, SQLite file `shoetracker.db`). In Docker the SQLite file lives on a named volume (`shoe-data`) so data survives restarts.

**Client** (`client/src/`):
- Vite + React 19 + TypeScript SPA, styled with Tailwind CSS v4 (via `@tailwindcss/vite`).
- `App.tsx` loads the shoe list on mount and renders `ShoeList`, `AddShoeForm`, and `LogRunForm` (`components/`).
- All server communication goes through `api/shoeTrackerClient.ts`, a small typed fetch wrapper that calls a relative `/api` base path and surfaces validation errors via `ApiError`, which wraps the API's `ProblemDetails` response. Add new API calls here rather than calling `fetch` directly from components.

### API endpoints

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/shoes` | List all shoes with computed total distance and retirement status |
| `GET` | `/shoes/{id}` | Get a single shoe |
| `POST` | `/shoes` | Create a shoe |
| `POST` | `/shoes/{shoeId}/runs` | Log a run against a shoe |

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

**Run everything via Docker Compose** (from repo root) — client at `http://localhost:8080`, proxying to the API:
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

**EF Core migrations** (from `src/ShoeTracker.Api/`) — `dotnet-ef` is a local tool pinned in `dotnet-tools.json`; run `dotnet tool restore` once if it's not on PATH:
```bash
dotnet ef migrations add <Name>
dotnet ef database update
```

## Tech stack

- Backend: .NET 10 / ASP.NET Core Minimal APIs, EF Core 10 with SQLite, xUnit
- Frontend: React 19 + TypeScript, Vite 8, oxlint, Tailwind CSS v4
- Infra: Docker (separate Dockerfiles for API and client), nginx reverse proxy, Docker Compose with a named volume for the SQLite database