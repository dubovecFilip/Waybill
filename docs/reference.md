# Every label Waybill uses

The values that end up stored in the database, the recordings and the exports,
what each one means, and whether it changes a verdict. The names are stable and
are what to match on when reading the data from outside.

Nothing here is a display string. The interface translates these into Slovak or
English; the stored value is always the identifier below.

## Outcome

Column `outcome`. How the job ended.

| Value | Meaning |
|---|---|
| `delivered` | Arrived and was paid for |
| `cancelled` | Cancelled in the game, or written off after a week unfinished |
| `unresolved` | The job stopped existing without ending: another profile loaded, the game quit, a crash |
| `reloaded` | A save from before the job was accepted was loaded, so in the game's own history the job no longer exists |

## Validation state

Column `validation_status`. One per delivery, derived from the flags.

| Value | Meaning |
|---|---|
| `accepted` | No flags |
| `review` | One or more flags, none of them grounds for refusal |
| `rejected` | At least one flag that is direct evidence the driving did not happen |
| `imported` | Came from a TrucksBook export, so there is no telemetry to check |

## Driving style

Column `driving_style`. Derived from how the delivery was actually driven, never
chosen in advance, so it can be worked out again for any past delivery from its
recording. It sorts deliveries into groups worth comparing with each other and
has no effect on the verdict.

| Value | Meaning |
|---|---|
| `clean` | Under 5% of driving time clearly over the limit, fewer than 3 fines, fewer than 3 collisions |
| `spirited` | Anything else |

"Clearly over" means more than 10 km/h above the posted limit, stored separately
as `hard_speeding_share`. Drifting a few km/h over is not what anyone means by
driving hard, so it does not count here; `speeding_share` remains the strict
measure of any excess at all. Single fines and collisions barely enter into it,
since one bad moment on a long haul says nothing about how someone drives; it
takes a handful of them before the pattern means anything.

## Validation flags

Column `validation_flags`, comma separated. Three of them reject; the rest are
visible and cost the delivery nothing.

| Flag | Raised when | Rejects |
|---|---|---|
| `teleport_detected` | Position moved faster than 400 km/h, unexplained by a job start, a gap or a loaded save | yes |
| `odometer_manipulation` | The odometer jumped more than 5 km in one tick, unexplained by a job start or a gap | yes |
| `distance_too_short` | Under 0.5 km driven | yes |
| `no_completion_event` | Outcome is `unresolved` | no |
| `abandoned` | Unfinished past the one week window and written off | no |
| `unstable_client` | More than two `client_gap` anomalies in one delivery | no |
| `implausible_top_speed` | Top speed over 180 km/h | no |
| `distance_inconsistent` | Odometer against speed times game time, outside 0.75 to 1.33 | no |
| `distance_mismatch` | Measured distance against the game's own figure on arrival, outside 0.8 to 1.25 | no |

## Anomalies

Everything the tracker noticed while driving, kept per delivery. Most are there
to explain why something that looks alarming was not treated as cheating. Only
the three marked below feed a flag.

| Code | What it marks | Feeds |
|---|---|---|
| `teleport` | A jump across the map no vehicle could drive | `teleport_detected` |
| `odometer_jump` | An odometer step too large for driving | `odometer_manipulation` |
| `client_gap` | A hole in the recording across which the game clock kept running at the rate the game reports, so Waybill was not polling | `unstable_client` past two |
| `paused_gap` | A hole across which the clock did not: a menu, photo mode, alt tab | |
| `fast_forward_gap` | The clock leapt further than the gap could account for: sleep, ferry, train, or loading | |
| `cargo_handling` | Such a leap with the cargo changing hands across it, so the game was loading or unloading rather than the driver resting | |
| `save_loaded` | The game clock went backwards, meaning an earlier save was loaded | |
| `resume_gap` | Distance recovered from the odometer for time the app was not running | |
| `telemetry_warmup` | The odometer reads 0 and the position is a placeholder while the world loads | |
| `vehicle_swap` | A quick job put the driver in a different truck, with the jumps that brings | |
| `job_start_transition` | A position jump inside the first 15 seconds of a job, which is the swap settling | |
| `odometer_settle` | An odometer jump inside that same window, or across a gap | |
| `odometer_reverse` | The odometer went backwards by more than 0.05 km | |
| `system_refuel` | The tank was filled by the game as part of a quick job, not by the driver | |
| `collision` | Damage rose by more than 0.1% in one tick | |
| `abandoned` | Written off after a week unfinished | `abandoned` |

## Timeline events

Table `events`, column `event_type`. The delivery's own history, at the moment
each thing happened.

| Type | Value holds | Detail holds |
|---|---|---|
| `fine` | Amount | The offence, by name |
| `tollgate` | Amount | |
| `ferry` | Price | Source and target |
| `train` | Price | Source and target |
| `refuel` | Amount paid | |
| `collision` | Damage step, in percent | |
| `rest` | Game minutes slept | |
| `save_loaded` | Game minutes rewound | |
| `trailer_coupled` | | |
| `cargo_loaded` | | |

`trailer_coupled` and `cargo_loaded` are the two halves of the load being on, and
which of them comes last depends on the kind of job:

| Job | Coupled | Loaded | The load is on when |
|---|---|---|---|
| Quick job | at the start | at the start | the job starts |
| Contract, the trailer waiting at the depot | when you hitch up | at the start | you hitch up |
| Your own trailer | before you set off | at the dock | it is loaded |

Only transitions are recorded, so whichever was already true when the job began
writes nothing. **The later of whatever was recorded is when the load was on**,
which covers all three without asking what kind of job it was, and a job that
recorded neither was loaded and hitched from the start.

Two things read that moment. It is where the map anchors a city, and it is where a
delivery's own line begins: the map and the exported sheet show the load's journey,
so getting to the trailer and driving to the dock are not part of it.

The map still draws that stretch, in the quieter style it uses for any other
driving off the job, because the driver went that way and the roads are real. It
simply belongs to no delivery, so it cannot be pointed at or opened. The exported
sheet leaves it off entirely: a waybill is about the consignment.

Those kilometres stay in the delivery's distance, since the game counts them
there, and are not repeated in the freeroam total. So a delivery's drawn line can
cover less ground than the figure beside it.

The offence is stored under the SDK's own name: `Crash`, `Speeding`,
`Speeding_camera`, `Red_signal`, `Wrong_way`, `No_lights`, `Avoid_sleeping`,
`Avoid_weighting`, `Avoid_Inspection`, `Illegal_trailer`,
`Illegal_Border_Crossing`, `Hard_Shoulder_Violation`, `Damaged_Vehicle_Usage` or
`Generic`. A recording keeps it as the raw number the game publishes, so a replay
names it on the way back in.

## Driving off the job

Tables `freeroam` and `freeroam_points`. A stretch driven with nothing on the
hook: between jobs, out to a trailer, or simply going somewhere.

The rule is what is on the hook, not whether a job exists. A load is being pulled
from the moment it is both hitched and loaded until the delivery ends; everything
else is freeroam, including your own trailer with nothing in it.

These are drawn on the map as a quieter line and are never clickable, because
there is nothing behind them to open. They carry no verdict and no flags: there is
no claim here to verify, so nothing is ever refused. Statistics show the distance
beside the deliveries rather than folded into them, and both together as everything
driven.

A stretch shorter than 0.5 km is dropped as manoeuvring, and a hole in the
recording ends one and starts another, so two evenings of driving are two lines
rather than one road drawn between them that was never taken.

## Distance, in two parts

`actual_distance_km` is everything the odometer counted for the job.
`distance_to_load_km` is the part of it driven before the load was on, whether
that was getting to the trailer or driving your own to the dock.

They are kept apart because the game plans its route from the load, not from the
driver. On a World of Trucks contract the odometer starts where the offer was
accepted, so the run out to the trailer inflates the total against a plan that
never described it. Measured across this history, the loaded leg agrees with the
planned figure to within a few percent on every delivered job, while the total ran
up to twelve percent over it.

The split is measured from the odometer as the job runs, never derived afterwards
from the recorded positions. Deriving it was tried and came out wrong by up to
three and a half times, because the world is compressed unevenly even inside one
drive: the same reason nothing else here treats world space as distance.

Progress on the live page is the loaded leg against the plan, with the run-up
shown as its own quieter stretch at the head of the bar.

## The coupled set

Columns `trailer_chain_type`, `trailer_owned` and `trailer_units`.

`trailer_chain_type` is the configuration under the game's own name, read from the
leading unit: `single`, `double`, `rmdouble` or `triple`. It is the game's idea of
it, so a three section car transporter reports `single`, being one articulated
vehicle rather than a road train.

`trailer_units` holds every coupled unit in hitching order, each with its
identifier, name, plate, body type, whether it is a `trailer` or a `dolly`,
whether it is owned, and the damage it took over that delivery measured from the
condition it was hitched in.

`trailer_owned` is true when any unit is the driver's own. Owned units are
identified the way trucks are, as `vehicle.something`, and carry a name; a trailer
handed over with the job is named for its type and has none.

`trailer_damage_pct` stays the worst across the set, which is the set's condition
and what the game itself shows.

## Job market

Column `job_type`. Which market the job was taken from, under the SDK's own name.
Empty on imports and on deliveries recorded before it was stored; a rebuild fills
those in from their recordings.

| Value | Meaning |
|---|---|
| `quick_job` | Quick job, with the truck provided |
| `cargo_market` | Cargo market at a company |
| `freight_market` | Freight market, driving your own truck |
| `external_contracts` | World of Trucks contract |
| `external_market` | World of Trucks market |

World of Trucks jobs carry no in-game deadline. Their window is a real one held on
the World of Trucks site, so the game reports the largest value the field can hold
and `minutes_late` stays empty.

## Delivery identity

Column `job_uid`. Computed from the game, the moment the job was accepted, and the
offer itself, so the same drive always derives to the same delivery. Storing one
that is already held replaces it rather than adding a second, which is what makes
a rebuild able to update history instead of having to delete it first.

## Source

Column `source`. Where the row came from.

| Value | Meaning |
|---|---|
| `waybill` | Tracked from telemetry |
| `trucksbook` | Imported from a TrucksBook CSV export |

## Recording lines

Field `kind` in a session recording. Either a periodic snapshot or the event that
prompted an extra one.

| Value | Written |
|---|---|
| `tick` | Once per second |
| `JobStarted` | The game reported a job starting |
| `JobDelivered` | Arrival, carrying the payout and the game's own distance |
| `JobCancelled` | Cancellation, carrying the penalty |
| `Fined` | A fine, with amount and offence |
| `Tollgate` | A toll paid |
| `Ferry`, `Train` | Transport taken, with price and both ends |
| `RefuelStart`, `RefuelEnd`, `RefuelPayed` | The three stages of refuelling; only the last carries the amount |
