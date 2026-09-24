# Roadmap

Last updated 2026-09-24. The epics below are listed in the order we'll build them. Each one records the decisions made so far. Anything marked *decided at implementation* is intentionally left open until we get there.

## Where we are

Shipped and live in `prod`:
- Shoe CRUD with a per-shoe retirement threshold (700 km by default).
- Manual run logging with edit and delete. Mileage and the over-threshold flag are computed server-side.
- Login with a cookie session, and per-user data isolation (another user's data returns 404). Accounts are created by the seed step or directly in the DB. There's no sign-up.
- HTTPS via Caddy on `milesleft.run`, deployed with Docker Compose.
- Automated daily SQLite backups with retention, plus manual snapshot and restore scripts.
- CI: API build and tests (integration tests cover every endpoint), client lint and build.

## Direction

- **Audience:** me plus a few friends for the next few months. We'll build features I'll actually use. Account management and ops polish can wait.
- **Strava is the primary source of runs.** My watch uploads runs to Strava automatically, and the app should pick them up without me re-entering anything. Manual logging stays as a small fallback for runs that aren't on Strava.

## Core concepts

These apply across the epics:

- **Run:** one row in the `Runs` table, whatever its source. `Source` is `Strava` (imported) or `Manual` (logged in the app). Strava runs keep their `StravaActivityId`.
- **Assigned / unassigned:** a run is either assigned to one of my shoes or unassigned (`ShoeId` is null). My imported Strava history starts out unassigned.
- **Ownership:** every run has its own `UserId`. Unassigned runs have no shoe, so ownership can't be derived through `Run.Shoe.UserId` any more, which is what happens today. Every run query filters on `Run.UserId`.
- **Shoe mileage:** `estimated km (E2) + sum of the runs assigned to the shoe`. It's always computed server-side, never stored as a running total. The over-threshold flag, retirement nudge and insights all use this figure.
- **Units:** the server stores and computes everything in km. Miles exist only in the client (E4).

## Epics

| Order | Epic | Summary | Size |
|---|---|---|---|
| E1 | Strava sync | Connect Strava and import my run history (E1a), then keep new runs flowing onto my default shoe (E1b) | L |
| E2 | Estimate mileage on existing shoes | Estimate past km on a shoe from when I got it, how often I wear it, and my run history | M |
| E3 | Shoe retirement lifecycle | Retire and un-retire shoes manually, nudged once past threshold. Retired shoes go in a collapsed section | S |
| E4 | Units: km / miles | A per-account unit preference, applied everywhere in the UI | S–M |
| E5 | Insights | Projected retirement date, distance per month, cost per km, rotation breakdown | M |

### E1 — Strava sync

**Data model changes** (made in E1a, used by everything after):
- `Run` gains `UserId`, a nullable `ShoeId`, `Source` and a unique, nullable `StravaActivityId`. The migration backfills `UserId` from each existing run's shoe, and existing runs become `Manual`.
- The run endpoints are nested under `/shoes/{shoeId}/runs`. Unassigned runs have no shoe, so `GET /runs` lists all of my runs (`?unassigned=true` for only unassigned ones). Reassigning is added in E1b.
- **Deleting a shoe** still deletes its runs, as today. Once Strava runs exist, deleting a shoe should unassign its Strava runs instead of deleting them, since they're Strava's. *Handled in E1b.*
- `UserScopingTests` is extended to cover unassigned runs and the new routes. CLAUDE.md's "ownership through `Run.Shoe`" note is updated.

**E1a — Connect and import history**
- **Before we start (me):** register a Strava API app at strava.com/settings/api. Callback domains are `milesleft.run`, plus `localhost` for dev. The client id and secret go in user-secrets locally and `.env` in prod, never in committed config.
- A "Connect Strava" OAuth flow (scope `activity:read_all`), and a way to disconnect.
- Tokens are stored per user, encrypted with ASP.NET Core Data Protection, and refreshed when they expire. The key ring lives on the `shoe-data` volume, and the backup/restore scripts include it. If the keys are ever lost, the fix is just reconnecting Strava. Persisting the key ring also means login sessions survive redeploys.
- On connect, my full history is imported as **unassigned** runs. `Run`, `TrailRun` and `VirtualRun` (treadmill) all count as runs. Importing is incremental and idempotent, keyed on `StravaActivityId`.
- Rate limits (100 requests per 15 min, 1,000 per day) aren't a concern: 200 activities per page, so years of history is a handful of calls.

**E1b — New runs flow in automatically**
- **Default shoe:** I mark one shoe as my current default, and each new Strava run is assigned to it automatically. With no default set, new runs arrive unassigned.
- **Reassign:** one click moves any run to another shoe or back to unassigned. An "Unassigned runs" list lets me sort out the leftovers, including old history runs I want to attribute to a shoe.
- **Getting new runs:** a Strava webhook (create/update/delete events) in prod, since `milesleft.run` is public. There's also a "Sync now" button, doing an incremental pull, for local dev and as a fallback if a webhook is missed.
- **Strava owns its runs:** a Strava run's date and distance are read-only in the app; only its shoe can change. Edits and deletions on Strava flow through.
- Manual logging remains for runs that aren't on Strava.

### E2 — Estimate mileage on existing shoes

**Problem:** Shoes I already owned before connecting Strava only count the runs assigned to them from now on. Their mileage and retirement status are too low. I rarely tagged shoes on Strava, so Strava's own per-gear distance isn't usable, and the past has to be estimated.

**Inputs (per shoe):**
- Roughly when I got it: the shoe's existing purchase date.
- How often I run in it: N times per week or per month.
- Optional *typical run length* for shoes used for one kind of run (long runs, intervals). If it's empty, my average run length in the window is used.

**Algorithm:**
- **Window:** from the purchase date to the day before the first run assigned to the shoe, or today if it has none. Estimated and assigned km never overlap.
- **Runs:** all my runs in the window, assigned or not. `weeks` = window length in weeks. `my runs per week` = number of runs ÷ weeks. `shoe runs per week` = N (a per-month input ÷ 4.35).
- **Without a typical length:** `estimate = total km in window × min(1, shoe runs per week ÷ my runs per week)`.
- **With a typical length:** `estimate = min(shoe runs per week × weeks × typical length, total km in window)`.
- **Example:** shoe bought 2 years ago, 1,500 km run in that time, ~3 runs a week, this shoe once a week. That's 1,500 × 1/3 ≈ **500 km**. As a long-run shoe at ~18 km: 1 × 104 × 18 = 1,872 km, capped at **1,500 km**.
- The logic lives in a `Services/` class, like `MileageCalculator`, and is unit-tested without Strava.

**Storage:** the shoe stores the inputs and the computed `EstimatedKm` snapshot. The estimate is recalculated when its inputs change, when the window changes (e.g. I assign an older run to the shoe), or on request. A normal page load never calls Strava.

**UI:** an "Estimate past mileage" section in the shoe form previews the window, my runs per week and the estimated km before saving. The card shows how much of the total is estimated.

### E3 — Shoe retirement lifecycle

`Shoe.IsRetired` already exists in the model, the API response and the card's "Retired" pill. Nothing can set it yet.

- **Manual, with a nudge:** an over-threshold card keeps its "Retire me!" pill and gains a one-click **Retire** button. Retired cards get **Un-retire**. Nothing retires automatically.
- **API:** retiring goes through the shoe update request or dedicated endpoints (*decided at implementation*), scoped per user like every shoe endpoint.
- **Display:** active shoes stay in the main grid. Retired shoes move to a collapsed "Retired" section below it, closed by default, with their run history intact.
- **Runs still allowed:** runs can be logged on or reassigned to a retired shoe. Shoe pickers (log run, reassign) list active shoes first and retired ones after, labelled.
- **Default shoe:** retiring my default shoe (E1b) clears the default, so new Strava runs arrive unassigned until I pick another.

### E4 — Units: km / miles

- **Saved on the account:** a `DistanceUnit` on `Users` (`km` or `mi`, default `km`). It's returned by `GET /auth/me` and changed through a small settings endpoint, plus a toggle in the header. Each friend picks their own.
- **Applies everywhere in the UI:** run entry and history, shoe totals, thresholds, the E2 typical run length, and E5's figures.
- **Client-only conversion:** the API contract stays in km. One client helper module converts in both directions, so the API, tests and Strava import are unaffected.
- **Rounding:** show 1 decimal. A value entered in miles must display the same after its round-trip through km (435 mi stays 435, not 434.9).

### E5 — Insights

Uses all my runs, Strava and manual. Per-shoe figures count only runs assigned to that shoe, plus its E2 estimate where relevant. Charts across all shoes show unassigned runs as their own "Unassigned" segment.

- **Projected retirement date:** per shoe, "reaches its threshold around March 2027", from its last ~8 weeks of assigned runs. It's hidden for retired shoes and for shoes with no recent use.
- **Distance per month:** a bar chart of monthly distance, stacked by shoe, plus "Unassigned".
- **Cost per km:** adds an optional price to each shoe and shows cost per km (or mile) over its total mileage, estimate included.
- **Rotation breakdown:** each shoe's share of my runs and distance over a recent period (e.g. the last 3 months).
- The calculations live in a `Services/` class. Charts use a small library or plain SVG (*decided at implementation*).

## Not planned for now

Known gaps we've deliberately left out:
- **Accounts:** no sign-up or password change. Email login is case-sensitive.
- **Richer run data:** duration/pace, notes, surface.
- **Ops:** backups are same-host only, deploys are manual, and there's no health check or uptime monitoring.
- **Client tests:** CI only lints and builds the client.
