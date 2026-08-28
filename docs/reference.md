# Every label Waybill uses

The values that end up stored in the database, the recordings and the exports,
what each one means, and whether it changes a verdict. The names are stable and
are what to match on when reading the data from outside.

Nothing here is a display string. The interface translates these into whichever
of its languages is set; the stored value is always the identifier below, and it
does not change with the language.

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

The list shows it as a coloured dot in the gutter rather than as a column of
words: green accepted, amber review, red rejected, blue imported. Four values
that are read at a glance do not need the width of a column, and hovering the
gutter names the one it is showing along with whatever was found.

## Driving style

Column `driving_style`. Derived from how the delivery was actually driven, never
chosen in advance, so it can be worked out again for any past delivery from its
recording. It sorts deliveries into groups worth comparing with each other and
has no effect on the verdict.

| Value | Meaning |
|---|---|
| `clean` | Under 5% of driving time clearly over the limit, and fewer fines and fewer collisions than the run allows |
| `spirited` | Anything else |

"Clearly over" means more than 10 km/h above the posted limit, stored separately
as `hard_speeding_share`. Drifting a few km/h over is not what anyone means by
driving hard, so it does not count here; `speeding_share` remains the strict
measure of any excess at all. The share is not scaled by anything: it is already
a proportion of the driving it was measured over, so it asks the same question of
twenty minutes as of twenty hours.

The two counts are scaled, because a count means nothing without the road it was
counted over. Three fines crossing town and three across fifteen hundred
kilometres are not the same driver, and judging both against one number made the
short run look like the wild one. A delivery is allowed 2 of each, plus one more
of each for every 500 km driven:

| Driven | Fines or collisions allowed |
|---|---|
| under 500 km | 2 |
| 500 km | 3 |
| 1500 km | 5 |
| 2500 km | 7 |

Single fines and collisions still barely enter into it, since one bad moment on a
long haul says nothing about how someone drives; it takes a pattern before it
means anything. Measured against this history the rate leaves every run anybody
would call spirited where it was, moves a busy three hundred kilometre hop into
them, and forgives a long haul its third knock.

## Sittings at the wheel

Table `session_spans`, one row per stretch of driving inside a recording: the
file name, the position in it, the first and last timestamp, and how many ticks.
Reading a recording to its end is the only way to learn when it ends, so the
answer is kept; a file already measured is never opened again, except the one
still being written, which grows.

Stretches rather than files, because telemetry can stop in the middle of a
recording: the app stays open and the game is closed for an hour. A recording is
cut wherever it goes quiet for more than three minutes, which is far more than
any stutter and far less than any real break.

A stretch holding less than half a minute of ticks is not counted at all.
Starting Waybill while the game is running writes a recording whether or not
anybody drives, so a look at yesterday's figures leaves a stretch one second
long: six of those inside one evening turned two interruptions into eight, and
none of them held a metre of road.

Sittings themselves are not stored. They are the stretches put back together
against `SessionGapMinutes` in the preferences, an hour by default. Nothing
about a sitting is written down, so the rule can be changed at any time and the
whole history regroups itself without a file being read again.

A delivery does not have to fit inside one sitting, so the two questions a
sitting answers are answered separately.

**What was finished here** is counted here, with what it paid: that is when the
job was done and when the money arrived.

**What was driven here** is measured here. Each delivery the window touches
contributes the share of itself that was actually driven inside it, taken from
how many of its recorded trip points fall in the window. The tracker writes one
point per second of driving, so the share is a share of the time at the wheel.
Sleeping needs no such treatment, since a rest is an event with a time on it,
and neither does free roaming.

The case this exists for: Yakima to Camp Verde took an afternoon, an evening and
the next morning, 15.6 %, 38.5 % and 45.9 % of the driving. Counted where it
began, two of those three sittings read as though nobody had driven at all.

## Electric trucks

Telemetry has no field for it. What it has is the identifier and the name, and
both games are consistent: an electric variant is the diesel one with `_e` on the
end of its identifier, and its name says so, "VNR Electric", "eActros 600",
"E-Tech T". `Trucks.IsElectric` reads those, so nothing is stored and every past
delivery is answered by the same test.

It matters beyond the marking. The tank capacity of an electric truck is reported
in kilowatt hours rather than litres, so the fuel figure is kilowatt hours too,
and converting it to gallons produced a delivery claiming 228.8 gal and 1.4 mpg
where the truth was 866.1 kWh and 268.8 kWh/100 mi. The delivery card and the
sheet both read it as energy now.

The statistics page keeps the two apart for the same reason: diesel and
kilowatt hours are summed separately there, and the battery figure appears only
once something has been driven on one.

## Awards

Seventy-two of them, defined in `Awards.All` and measured in `Awards.Measure`.
Each carries an identifier, a name, the shelf it sits on, a threshold, whether it
repeats, whether it is secret, and what it is worth in Waybill experience.

Names are left in English on purpose. They are titles rather than labels, the way
a stamp in a passport is, and the sentence under each one, which is the part that
explains anything, is translated into all five languages.

Measured by walking the deliveries in the order they happened rather than by
adding up a column. Streaks, days and milestones all need the order: ten clean
deliveries in a row is a different fact from ten clean deliveries, and the
delivery that carried a milestone over is worth naming.

Repeatable awards count every time. The row stores `times_earned`, and the count
only ever climbs: if a rule is rewritten later and measures fewer, what was
written down stands.

Experience is Waybill's own and unrelated to the experience the games pay, which
is stored per delivery in `xp`. It is called XP anyway, on the grounds that every
driver already knows what XP is and a cleverer name would need explaining. A level costs fifty more than the one before it,
`25 * (n^2 + n - 2)` in total by level `n`, so the first is a hundred and the
tenth is five hundred and fifty.

Imported rows are left out. One from TrucksBook carries a distance and a payout
and nothing else, no damage, no fines, no route.

Only the pass that runs when a delivery has just finished says anything in the
strip at the foot of the sidebar. Every other pass is quiet, so a first run or a rebuild does not
announce forty awards at once for driving that happened weeks ago. That pass runs
before the history reloads, because the reload runs a quiet pass of its own and a
quiet pass writes an award down without saying anything.

### What the telemetry decides

Some of these are read off the two ends of a delivery rather than off the route,
because the route is a line of positions and nothing in it says which country or
which road it was on.

- *Borders and state lines* are counted when the two ends are in different
  regions. A delivery that passes through a third country on the way counts once.
- *The Channel* is a delivery with Britain at one end, the mainland at the other,
  and a ferry or a train on the bill. Nothing says which water was crossed.
- *East and west* are a list of country codes, and *desert* and *mountain* a list
  of state codes. Both are judgements rather than data.
- *Farm cargo* is matched on the cargo name, which is all either game says about
  what a load is.
- *Night* and *dawn* use the clock on the wall at the moment the delivery
  finished, not the clock in the game. It is a fact about when the driver was
  driving.
- *Heavy* is twenty-four tonnes, which is where a load starts being the reason
  the drive was slow.
- *On time* is within fifteen game minutes of the deadline either way, and awards
  that need it skip any delivery where the game never said what the deadline was.
- *Speeding fines* are told from other fines by the offence the game names, which
  is stored with the event.
- *Tollgates* are counted from the events rather than from the money, so a free
  gate still counts.

Five of the awards this was drawn from are not built, because nothing in the
telemetry could decide them honestly: a delivery crossing three countries, island
and Mediterranean routes, coast to coast, interstate driving, truck stops and
weigh stations. Each would need the route matched against a real map, which is
the decision the map page already refused to make.

## Condition, which is wear as well as damage

Both games keep one number per component, and the SDK reports that one number:
`truck.wear.*` and `trailer.wear.*`, zero to one. It rises when something is hit and
it rises with the kilometres, and nothing in the telemetry separates the two. The
damage a delivery reports is the difference between the set's condition when the load
went on and its condition at the drop, so it holds both.

The impacts can still be told apart, because they arrive as steps rather than as a
creep. That is exactly what the collision detection watches for, so every impact is a
line on the delivery's timeline with the share of damage it did. Measured on a real
drive: Rijeka to Oslo, the truck's condition moved 1.46 % over two thousand
kilometres, of which one collision accounts for 0.29 % and the remaining 1.17 %
crept up a hundredth at a time. The trailer moved 4.61 % without a single step in it,
which is a set that was worn rather than damaged.

The set's condition is read from the last tick that still had a trailer in it. The
game drops the trailer out of telemetry the instant the load is handed over, and the
tick carrying the delivery event has none, so reading the condition from that tick
reads zero and reports a delivery that scratched nothing. That is what a trailer
arriving at 4.6 % was written down as before this was found.

## The region a city is in

Not stored and not reported by either game. `Places` holds a table of city names
against a state code for American Truck Simulator and a country code for Euro
Truck Simulator 2, and the window and the sheet look a city up in it when the
`CityRegions` preference is on.

Columns `source_city_id` and `destination_city_id` hold what the game calls each
end when it is talking to itself. The lookup asks by identifier first, since two
cities can share a name inside one game and never an identifier, and falls back
to the name for rows recorded before identifiers were kept. They come out of the
recordings, so a rebuild fills them in.

The table is deliberately incomplete: mod maps are not in it, and neither are
names that appear twice in one game. A lookup that misses adds nothing to the
city, which is the honest answer. Nothing is derived from it and nothing depends
on it, so it can be corrected or extended at any time without touching stored
data.

## Rest

Not every jump of the clock is a sleep. Charging a battery at the roadside,
having the truck repaired and taking a job out of a menu all move the clock
forward by hours, and all three used to be written down as rest: one delivery in
an electric truck came back claiming four sleeps of two hours, when three of them
were the battery going from nothing to full and the fourth was a quick job.

The game tells them apart itself. `NextRestStop` counts the minutes the driver
has left before they must sleep, and a sleep is the only thing that puts that
number back up; everything else leaves it falling by exactly as much as the clock
advanced, because the driver was awake for every minute of it. A jump that is not
a sleep is recorded as the anomaly `awake_gap` and counted as nothing.

One more guard sits in front of that: a sleeping truck does not move. A quick job
puts the rest timer up as well, since the game hands over a fresh driver with it,
but it hands over a different truck too and the odometer lands on that truck's own
reading, 393 639 km becoming 7 387 in a single tick.

A sleep in these games is a whole number of hours. The measurement is not: a rest
is read as the distance the game's clock moved between two samples taken a second
apart, and either sample can sit a moment either side of the sleep itself, which
is how ten hours came out as 601 and 602 minutes.

So a jump within five minutes of a whole hour is recorded as that whole hour, and
anything further off is kept exactly as measured. Five minutes is wider than any
sampling slop and nowhere near wide enough to turn a short stop into an hour.

Subtracting the time that would have passed during the tick anyway was tried
first and made it worse, turning fifteen hours into 899 minutes and one ten hour
sleep into 594: the clock does not run during the load that follows a sleep, so
there was no ordinary passage there to take off.

## Validation flags

Column `validation_flags`, comma separated. Two of them reject on their own, one
rejects in company; the rest are visible and cost the delivery nothing.

| Flag | Raised when | Rejects |
|---|---|---|
| `teleport_detected` | Position moved faster than 400 km/h, unexplained by a job start, a gap, a loaded save or a crossing | only with `distance_mismatch` or `distance_inconsistent` |
| `odometer_manipulation` | The odometer jumped more than 5 km in one tick, unexplained by a job start or a gap | yes |
| `distance_too_short` | Under 0.5 km driven | yes |
| `no_completion_event` | Outcome is `unresolved` | no |
| `abandoned` | Unfinished past the one week window and written off | no |
| `unstable_client` | More than two `client_gap` anomalies in one delivery | no |
| `implausible_top_speed` | Top speed over 180 km/h | no |
| `distance_inconsistent` | Odometer against speed times game time, outside 0.75 to 1.33 | no |
| `distance_mismatch` | Measured distance against the game's own figure on arrival, outside 0.8 to 1.25, and only on jobs the game reports as 10 km or more | no |

### A jump is not a verdict on its own

The rule used to be that a `teleport` anomaly rejected the delivery outright. The
drive that ended that rule was an ordinary one: Rijeka to Oslo, two thousand
kilometres, with the Rostock to Gedser ferry in the middle. Seven seconds after the
game charged for the crossing it put the truck down on the far shore, 1.15 km of
world space at an implied 617 km/h, and Waybill called a real delivery a fake.

Two things came of it. A jump within two minutes of a ferry or a train is recorded
as a `crossing` and feeds nothing, since the game said itself what it was doing.
And a jump that is not explained that way no longer rejects on its own: it needs
the distance evidence to agree, because the distance is what says whether the drive
happened. That same delivery measured 2082.9 km against the game's own 2084.

## Anomalies

Everything the tracker noticed while driving, kept per delivery. Most are there
to explain why something that looks alarming was not treated as cheating. Only
the three marked below feed a flag.

| Code | What it marks | Feeds |
|---|---|---|
| `teleport` | A jump across the map no vehicle could drive | `teleport_detected` |
| `crossing` | The same jump, within two minutes of a ferry or a train being paid for, which is the game carrying the truck across | |
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

A fine with the offence `Crash` is folded into the collision it belongs to rather
than listed beside it. The game reports both within a second of each other, and
two marks for one moment read as two things going wrong; the collision keeps its
place and carries the amount. Both rows stay in the database, so the fine totals
are unaffected and a reader from outside sees what the game actually said.

The interface draws each type as a sign rather than writing it out, and the same
sign is used everywhere the type appears. `train` shares the ferry's, being the
same thing to a driver, and `fine` takes a different one when the offence is
`Speeding`. *Help → Legend* names them all.

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

Three things read that moment. It is where the map anchors a city, it is where a
delivery's own line begins, and it is where the timeline starts: the map, the
sheet and the list of what happened all show the load's journey, so getting to the
trailer and driving to the dock are not part of any of them.

**Nothing before it is recorded at all.** A fine picked up on the way out to the
trailer, or a refuel taken before the dock, is not the consignment's history.
`trailer_coupled` and `cargo_loaded` are written, because that is how the moment
is found, but neither is shown: they mark the beginning rather than something that
happened along the way.

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

## A delivery begins at the load

`actual_distance_km` is what the odometer counted from the moment the load was on
to the moment it came off. Everything before that is driving with an empty hook and
is kept as a stretch of free driving, the same as any other.

This is what makes the five ways of taking a job into one thing. The games disagree
about when a job is running: a quick job and a World of Trucks contract start the
moment the offer is taken, with the trailer possibly a city away, while the freight
and cargo markets only start once the driver has reached the company. Measuring from
the load makes every delivery the load's own journey, which is also what the game's
planned distance describes and what the delivery screen reports on arrival.

Measured across this history: rebuilding it under this rule moved 651 km out of 26
deliveries and into free driving, exactly the run-up those deliveries had recorded,
and left the other 18 untouched because their load was already on when the game said
the job had begun. The largest single case was a delivery of 553 km of which 379 were
the drive out to the trailer.

`distance_to_load_km` is what that run-up used to be recorded as, and it stays in the
schema for rows written before this. Nothing fills it now.

The load going on is the later of two moments, since they arrive in either order: the
trailer being coupled, and the cargo being reported aboard. Pulling your own trailer
you are hitched long before the dock; on a contract the trailer is waiting already
loaded and the coupling is the whole of it.

Distance is measured from the odometer as the job runs, never derived afterwards from
the recorded positions. Deriving it was tried and came out wrong by up to three and a
half times, because the world is compressed unevenly even inside one drive: the same
reason nothing else here treats world space as distance.

## The coupled set

Columns `trailer_chain_type`, `trailer_owned` and `trailer_units`.

`trailer_chain_type` is the configuration under the game's own name, read from the
leading unit: `single`, `double`, `rmdouble` or `triple`. It is the game's idea of
it, so a three section car transporter reports `single`, being one articulated
vehicle rather than a road train.

`trailer_units` holds every coupled unit in hitching order, each with its
identifier, name, plate, body type, whether it is a `trailer` or a `dolly`,
whether it is owned, the damage it took over that delivery, and `StartDamage`,
the condition it was hitched in that the damage is measured from.

`trailer_owned` is true when any unit is the driver's own. Owned units are
identified the way trucks are, as `vehicle.something`, and carry a name; a trailer
handed over with the job is named for its type and has none.

`trailer_damage_pct` stays the worst across the set, which is the set's condition
and what the game itself shows.

`truck_damage_pct` and `trailer_damage_pct` are what the delivery added;
`cargo_damage_pct` is what the game reported outright on arrival.
`truck_damage_start_pct`, `trailer_damage_start_pct` and `cargo_damage_start_pct`
hold what each was in at the moment the load went on, so both ends of the run can
be shown rather than only the difference between them. They are null, never zero,
on rows recorded before they were kept: zero would be a claim that the set left
undamaged. A rebuild fills them in from the recordings.

The name a unit is shown under is worked out for display only; nothing about it is
stored, so improving it improves every past delivery at once. An owned unit uses
the name its driver gave it. One handed over with the job has none, so the
identifier is read instead: `blade_hauler.chassis_40x2esii` gives its body type
and the length in feet, shown as *Blade hauler, 40 ft*. A body type beginning with
an underscore is the game's own marker rather than a description and is skipped,
which is what keeps `_oversize` out of the name. A dolly says it is a dolly.

## Special transport

Column `special_transport`. True when the job was an oversize load, which the game
reports on the job itself.

It changes nothing about the verdict, the distance or the pay. It is shown because
it changes what the drive was: an escorted convoy at half the usual speed is not
the same delivery as the same route with a curtainsider. The interface marks it
with hazard stripes: down the card, down the row's gutter beside the verdict dot,
and as a band across the head of the exported sheet. The list can be narrowed to
one kind or the other, since an oversize haul is worth comparing against another
oversize haul rather than against a curtainsider.

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
