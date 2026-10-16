# ShoeTracker

Tracks a runner's mileage per pair of running shoes so worn-out shoes get retired on time. One context: a runner, their shoes, and the runs logged against them.

## Language

**Training Log**:
A user's complete record of runs and shoes, and the single owner of the rules for changing it (who owns what, which runs may be edited, what happens to runs when a shoe goes away, where new imported runs land, what counts as valid).
_Avoid_: runner's log, run service, shoe service

**Run**:
A single recorded running activity with a date and distance, belonging to a user and optionally assigned to one shoe.
_Avoid_: activity (that's Strava's word for the not-yet-imported thing)

**Shoe**:
A pair of running shoes whose accumulated run distance counts toward retirement.
_Avoid_: gear

**Strava run**:
A run imported from Strava. It is owned by Strava's record: locally read-only apart from which shoe it is assigned to.

**Unassigned run**:
A run not attributed to any shoe; its distance counts toward no shoe's mileage.
_Avoid_: orphaned run

**Default shoe**:
The shoe newly arriving Strava runs are assigned to. A user may have none, in which case new Strava runs stay unassigned.

**Retirement threshold**:
The distance (default 700 km) at which a shoe is considered worn out and due for retirement.
_Avoid_: mileage limit
