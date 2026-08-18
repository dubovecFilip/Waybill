# Waybill

A local delivery tracker for **Euro Truck Simulator 2** and **American Truck
Simulator**. It detects the start and the end of a job on its own, writes it to a
local database and shows statistics. No account, no internet, no second program.

A *waybill* is the document that travels with a shipment and carries the route,
the cargo and the sender. That is exactly what this app keeps about every drive.

![The delivery history](assets/deliveries.png)

## Why it exists

TrucksBook invalidates a delivery if lane assist was switched on during it.
Waybill takes the opposite position:

> **A delivery is never invalidated because the driver used an assist.**

Assists, cruise control, speeding, collisions and fines are stored as metadata
and shown in statistics, never as grounds for refusing a delivery.

## How a delivery is judged

Every finished delivery gets one of three states.

| State | Meaning |
|---|---|
| `accepted` | Nothing unusual was found |
| `review` | Something is worth a look, but nothing suggests the driving was faked |
| `rejected` | Direct evidence that the driving did not happen |

**Rejection is reserved for a delivery being claimed without the driving behind
it.** There are three such cases, and no others:

* a teleport, meaning a jump across the map that no vehicle could have driven
* an odometer that moved further in one instant than driving can account for
* a distance of essentially zero

Everything else is a flag. A flag is visible on the row and says what it is,
and that is all it does: the delivery keeps its distance, its payout and its
place in the statistics, which count every row whatever its state. Reaching
review costs nothing.

A verdict is never left unexplained. Hovering it in the list names what was
found, and the delivery's own card opens with *Why this verdict*: each flag in
words, with the figures behind it where two measurements disagreed, and a line
saying plainly that none of it refuses the delivery.

What lands in review is, for instance, a job that stopped existing without
ending, one abandoned past its window, a top speed no truck reaches, or the two
independent distance measurements disagreeing with each other or with the figure
the game reports on arrival.

The distinction that matters: **not finishing a delivery is not cheating**.
Switching to another profile, quitting the game and never coming back, a crash
mid drive: none of that claims a delivery, so none of it is refused. It is
recorded for what it is, an unfinished drive, with the kilometres actually
driven kept.

The other half of the same idea is that ordinary play is recognised rather than
punished. Pausing into a menu or photo mode, sleeping in the cab, taking a ferry,
loading an earlier save, being teleported into a company truck by a quick job,
restarting the tracker mid delivery: each of these looks alarming in raw
telemetry, and each is identified for what it is instead of counting against the
driver.

Every state, flag and anomaly is listed with its meaning in
[`docs/reference.md`](docs/reference.md).

## Features

* Opens on an overview: the drive in progress, the last few deliveries, settings
* Detects the start and the end of a job with no manual input
* Measures distance, fuel, speed, damage, fines, tolls and ferries
* Records an event timeline, so the exact moment of a fine or a collision is kept
* Keeps every unit of a double or a triple, each with the damage it took
* Says which market a job came from, and whether the trailer was your own
* Explains every verdict in words rather than leaving a label on the row
* Draws where each delivery went, with every fine and collision marked on it
* Draws it the way it was driven, so the order things happened in is visible
* Marks each thing that happened with a sign of its own, and explains every sign
* Names a trailer for what it is instead of for the file it came out of
* Marks an oversize load as one, on its card and in the list
* Draws the whole history as one map, built entirely from your own drives
* Writes a delivery out as a printable A4 waybill, route and stamp included
* Keeps the driving between jobs too, as distance and as lines on the map
* Resumes an interrupted job after a crash or after quitting mid drive
* Launches the game from the window, including a telemetry plugin check
* Shows the current delivery on Discord, over the local pipe, with no account
* Imports history from TrucksBook
* Stores in SQLite, exports to CSV and JSON, backs up and restores
* Interface in English and Slovak

## Requirements

* Windows
* [.NET 9 SDK](https://dotnet.microsoft.com/download) to build from source
* The RenCloud telemetry plugin, bundled in [`third-party/`](third-party/README.md)

## Installation

Build from source:

```bash
dotnet build src/Waybill
```

The app then starts from `Waybill.exe` in
`src/Waybill/bin/Debug/net9.0-windows/`.

The first step after launching is *Play → Install telemetry plugin*, which asks
for `Win64/scs-telemetry.dll` from `third-party/`. Without the plugin the game
publishes no telemetry and Waybill has nothing to track.

### Standalone .exe

Run from the repository root, since the paths in the command are relative:

```bash
dotnet publish src/Waybill -c Release -r win-x64 -p:PublishSingleFile=true -o dist
```

The result is a single `dist\Waybill.exe` of about 50 MB that runs on a machine
without .NET installed. It can be moved anywhere: the database and the recordings
live in `%LOCALAPPDATA%\Waybill\`, independent of where the exe sits.

## Usage

Start order does not matter, the app connects to the game as soon as it finds it.
Jobs are detected and saved without any input.

Five pages down the left. *Overview* is where the window opens, and it is the
only page that answers "what is going on" rather than a question you have to
already have. It holds the drive in progress with its route drawn beside it as
it goes, the last five deliveries, and the three settings worth reaching for.
Nothing on it is new: everything has a page of its own, one click away, and the
tracker's log stays on *Current job* where it belongs. *Deliveries* is the history. Every row carries its
verdict as a dot in the gutter on the left, and an oversize load carries hazard
stripes beside it; hovering the gutter says both in words. Clicking a column
heading sorts by it, and the order chosen is kept: opening a delivery and coming
back leaves the list the way it was left rather than back on date order.

Above the list, a search box and two switches. Each switch has a middle position
meaning both, one end for **ETS2** and the other for **ATS**, and one end for an
ordinary load and the other for an oversize one. They are the two questions a
history is actually read with, and each is one click away from either answer.
There is no filter by verdict: it is a dot on every row already, and on a
history where almost everything is accepted, asking for "only the accepted ones"
is asking for the list you are looking at.

*Current job* is the same drive in the tracker's own terms: the route, the cargo, how far
along the drive is, and a log of what has happened, each entry with its figure,
so a fine reads `Fine: 700 $ (crash)` rather than just `Fined`.

Double clicking a delivery, or pressing Enter on it, opens its own card. It starts
with why it got the verdict it did, then every figure the tracker kept.

![A delivery on its own card](assets/delivery-card.png)

*What happened along the way* slides a column out from the right: the route on
top, the timeline underneath it. Both are worth reading when something went wrong
and worth nothing when nothing did, so they stay out of the way until asked for.

![The route and the timeline](assets/delivery-route.png)

*Statistics* is the whole logbook at a glance, on one screen with no scrolling.

![The statistics page](assets/statistics.png)

Distance driven with nothing on the hook has its own tile there, beside the
deliveries rather than folded into them, and the delivery figure carries both
together underneath it.

## What the window says without words

A good deal of it is drawn rather than written. The dot in a row's gutter is its
verdict, the colour of a route is speed, hazard stripes mean an oversize load,
and each thing that happened on a drive gets a sign of its own on the timeline: a burst for an impact, a dial for a speeding fine, a note for any
other one, a drop, a moon, a hull, a barrier, two chevrons back for a save
loaded.

The signs are drawn by the app itself rather than taken from a font, because the
glyphs for most of them live in fonts that may not be installed, and a missing one
comes out as an empty box exactly where the meaning was.

*Help → Legend* names all of them in one window, along with the marks on the map
and the two shades in the progress bar. The samples in it are painted by the same
code the rest of the window paints with, so it cannot quietly drift away from what
it explains.

![Every mark, with what it means](assets/legend.png)

One sign is deliberately missing. A fine for a crash is not listed apart from the
crash: the game reports both, and two marks a second apart for one moment read as
two things going wrong. The impact keeps its place and carries the amount.

## Doubles and triples

The game shows one condition for the whole set however many units are behind the
truck. Telemetry has them separately, and Waybill keeps each one.

The *Trailer* line folds the set away: closed it says what the set is, opened it
lists every unit in the order they are hitched, with its plate and the damage it
took. A dolly is named as a dolly, so counting or averaging does not treat the
converter as cargo capacity, and a trailer you own says so.

A unit you own carries the name you gave it. One handed over with the job has
none, and the game identifies it by the file it came out of, so
`blade_hauler.chassis_40x2esii` is read for what it holds: the body type and the
length, giving *Blade hauler, 40 ft*. That is what a driver would say about it,
and it is what fits in a column.

![The coupled set, opened](assets/trailer-chain.png)

The configuration comes from the game itself, which is where `single`, `double`,
`rmdouble` and `triple` come from. It is worth knowing that the game reports its
own idea of it: a three section car transporter calls itself `single`, because it
is one articulated vehicle rather than a road train.

Splitting the set apart is not only bookkeeping. On one triple the leading unit
took fifteen times the body damage of the others while the wheels wore evenly
across all three, which is the difference between clipping something and simply
driving a long way. One figure for the set hides that entirely.

## The map

Position has been recorded once a second since the first delivery, so every drive
already had a shape long before anything drew one.

A delivery's card shows its own route, coloured by speed, over every other route
the profile has driven. Fines, collisions, refuels and rests are marked on it,
each tied to the position the truck was in when it happened. The *Map* page shows
the same thing without a delivery singled out: every drive of one game at once,
where pointing at a route names it and clicking one opens its card. It is a second
way into the history, since the list answers when something was driven and this
answers where.

![The map of everywhere driven](assets/map.png)

Wheel zooms, dragging moves, double clicking fits. Two buttons sit over the top
right: what to draw, and fit the view. The eye beside each layer says whether it
is being drawn, and a layer that is hidden cannot be picked either, so nothing
opens from a line that is not on the screen. Cities stay visible whatever else is
turned off, since they are what makes the rest readable.

The same map, smaller, sits in the panel a delivery's card slides out, where a
third button opens it full screen.

### A route draws itself

Opening the panel draws the line from the pickup to the drop rather than showing
it finished, and the play button does it again. It is time being replayed rather
than distance: the points are a second of driving apart, so the line runs on an
open road and dawdles through a city, exactly as the drive did. Pins arrive as
the line reaches them, and a marker rides the head of it until it gets to the
far end.

That is worth more than it sounds. A finished line says where the drive went and
nothing about the order it happened in, and the same picture with a collision
near the end reads quite differently from one with a collision on the way out.
Touching the map at all ends the replay and shows the whole route, because a
drawing still being drawn is in the way the moment you want to look at it.

### There is no real map underneath, on purpose

The game's world is not a scaled United States. Measured across nineteen
deliveries, some pairs of cities sit thirteen times closer than reality and others
thirty, which is a difference of more than twice within the same map. SCS did not
shrink a country; they laid out a road network that plays well and put the cities
where that worked.

So no projection fits. Fitting the closest one anyway leaves cities about thirty
kilometres out under cross validation, and neither a quadratic fit nor a spline
through the cities as control points does better. Thirty kilometres is roughly
twenty minutes of driving: a state border drawn that far off would put the truck
in the wrong state, which is exactly the sort of small lie this project tries not
to tell.

What is drawn instead comes entirely from the driver's own data, where every
position is exactly where the game put it:

* **The routes already driven** are the background. They are the one backdrop that
  cannot be wrong, and the picture fills in as more gets driven.
* **Cities are learned** from the jobs themselves. Each names the city it loaded
  in and the one it unloaded in, and the recording says where the truck was. A dot
  is really the middle of the depots used there, so a city seen once is one depot
  wearing the city's name.

Nothing on the map ever reports a distance, and it never will. The length of a
line on it is not kilometres; the odometer answers that.

### A route is the load's journey

It begins where the load went on, not where the job was accepted. On a contract
that is where you hitched up to a waiting trailer; pulling your own it is the dock,
since you were coupled long before that; on a quick job the truck is set down at
the depot already loaded, so it is the start.

Whichever it was, the recording usually opens with a drive that is getting to work
rather than the consignment moving. That stretch is still drawn, in the same quiet
style as any other driving off the job, but it belongs to no delivery: it cannot be
pointed at or opened, and it is not part of the line the delivery owns.

Nothing that happened during it is recorded either. A fine picked up on the way to
the trailer is not the consignment's history, so the timeline starts where the load
does, and the hitching itself is not listed on it: it is the first line's own
beginning, not something that happened along the way.

The kilometres are still counted, and now counted separately. A delivery's card
shows the two legs on their own line, and the progress bar on *Current job* draws
the run-up as a quieter stretch at its head so the loaded part reads against the
plan. That matters more than it sounds: the game plans its route from the load, so
measured across the deliveries here the loaded leg lands within a few percent of
the planned figure on every delivered job, while the total ran as much as twelve
percent over it.

On a quick job, where the truck is put down at the depot already loaded, there is
no run-up at all.

### Driving that belongs to no delivery

Everything driven with nothing on the hook is kept too: between jobs, out to a
trailer, or simply going somewhere. It is drawn as a quieter line and is not
clickable, because there is nothing behind it to open, and it counts towards its
own total rather than being folded into the deliveries.

On the history here that is 292.7 km over five stretches, about five percent of
everything driven. Most of it retraces roads the deliveries already cover, which
is the point: it is the same network, filled in.

### What is not drawn as driving

Two stretches are deliberately shown as a dashed break rather than a line. The
first point of a job is where the driver stood when the offer was accepted, which
on a quick job is another city entirely, and a ferry, a train or loading an
earlier save moves the truck mid drive. None of them were driven along.

## Saving a delivery as a sheet

*Save sheet* on a delivery's card writes it out as the document the app is named
after: A4 upright, the form printed and the figures written in. Shipper and
consignee, the coupled set in hitching order, the route sketched in the same pen,
the remarks entered along the way, and the verdict struck as a rubber stamp.

![A delivery written out as a waybill](assets/waybill-sheet.png)

It is rendered at 300 dpi, so it prints as well as it posts. A delivery with more
than a sheet's worth of units or remarks runs onto a second one, which reprints
the heading and carries the stamp at its foot, and the files are numbered.

This is the only place the paper idea lives, and that is deliberate. As a skin for
the window it would have cost the map its zooming and clicking, or left two
different maps in the app, and a fixed sheet cannot hold seven trailer units. A
file has none of those problems: it is a fixed size by definition, nobody expects
to click it, and running onto a second sheet is what paper has always done about
too much content.

From a script:

```bash
Waybill.exe --export-sheet <id> [path.png]
```

## Discord

*Settings → Discord* puts the current delivery on the Discord profile: the route,
the cargo, how far along the drive is, and a counter of how long it has been
running. Between jobs it says so, and with the game closed it shows nothing at
all.

It talks to the Discord client on the same machine through its local pipe, so
nothing leaves the computer and no account is involved. Discord not running
simply means nothing is shown.

Setting it up takes one value. Create an application at
[discord.com/developers](https://discord.com/developers/applications), whose name
is what appears above the presence, and paste its Application ID into *Settings →
Discord → Application ID*. The ID is public by design and is not a password.
Until it is filled in, nothing is sent anywhere.

Uploading three images to that application under *Rich Presence → Art Assets*,
named `ets2`, `ats` and `waybill`, gets the icons as well. Without them the text
still shows.

## Command line

The window is the usual way to use it, but everything also works from a script:

```bash
Waybill.exe --list [n]                  # recent deliveries
Waybill.exe --stats [days]              # summary, overall or for a period
Waybill.exe --export csv|json [path]    # export the history
Waybill.exe --import-trucksbook <csv>   # import history from TrucksBook
Waybill.exe --backup [path]             # back up the database
Waybill.exe --restore <path>            # restore from a backup
Waybill.exe --export-sheet <id> [path]  # one delivery as an A4 waybill, 300 dpi
Waybill.exe --rebuild                   # recompute deliveries from recordings
Waybill.exe --replay <recording>        # replay an old recording
Waybill.exe --test-resume <recording> <line>   # test resume after a restart
```

## Where the data lives

Everything sits in `%LOCALAPPDATA%\Waybill\`, outside the project folder, so a
rebuild or a `dotnet clean` cannot take any of it with it.

| What | Where |
|---|---|
| Delivery database | `deliveries.db` |
| Backups | `backups/` |
| Raw telemetry recordings | `sessions/` |
| Job in progress | `in-progress.json` |
| Settings, including the Discord application ID | `settings.json` |

Recordings are packed into `.gz` once a session ends, roughly 13x less space.
They are never deleted: they are the input for `--rebuild` and `--replay`, both
of which read them packed or unpacked.

A delivery's identity is computed from the game, the moment the job was accepted
and the offer itself, so the same drive always derives to the same delivery. That
is what lets a rebuild update the history rather than having to delete it first.

## Units

By default they follow the game. ATS uses imperial (mi, gal, mph, $) and ETS2
metric (km, l, km/h, €). The *Units* menu forces one system for both.

The database always stores metric and converts only for display. The history
therefore does not depend on which setting was active during the drive, and
switching redraws old deliveries too.

## How distance is measured

Distance follows the odometer, the same unit system the game itself and the job
offer report. World position is stored separately, for teleport detection and for
drawing the route later. Details in [`docs/measurement.md`](docs/measurement.md).

## Importing from TrucksBook

*Data → Import history from TrucksBook*, then pick the CSV export. The import is
idempotent and keyed on the TrucksBookID, so the same file can be run repeatedly
without producing duplicates.

The export uses the units of that profile and every value carries its unit with
it (`157 mi`, `5.9 mpg`), so conversion follows what is actually in the file.
Imported deliveries get the `imported` status, because there is no telemetry
behind them to verify.

Deliveries that TrucksBook counted as 0 distance, meaning the ones it refused,
are imported with their planned distance and a note. Waybill counts them.

## Development

The recordings in `sessions/` double as regression tests. After a change in the
tracker:

```bash
Waybill.exe --replay <recording>
```

and compare the numbers. `--test-resume` additionally simulates a restart in the
middle of a drive and compares the result against one continuous run.

`--rebuild` is worth running after every detection fix, because old rows
otherwise keep the verdict issued by the version current at the time.

It only touches the periods the recordings cover. A delivery driven at a time no
surviving recording spans is kept exactly as it is rather than deleted in the
hope something replaces it, and the run reports how many were kept that way.
Imported rows are left alone too, as there is nothing to recompute them from.
Everything is replayed before anything is deleted, so an unreadable recording
cannot leave the history half restored, and a backup is taken first regardless.

## Layout

```
src/Waybill/
├── Tracking/       job state machine, SDK adapter, engine, formatting
├── Storage/        SQLite (deliveries, events, trip_points), TrucksBook import
├── Integrations/   Discord Rich Presence over the local pipe
├── SCSSdkClient/   vendored C# SDK client (MIT, with local fixes)
├── GameLauncher.cs finding and launching the games through Steam
├── RouteView.cs    drawing routes, cities and event pins
├── RouteGeometry.cs where a recording stops being a drive, shared by both
├── WaybillSheet.cs a delivery painted as an A4 document
├── MainForm.cs     the window
└── Program.cs      CLI and entry point
assets/             logo and icon source
docs/               vision, roadmap and technical notes
third-party/        telemetry plugin for the game
archive/            retired, no longer used
```

## Status

Working: automatic tracking and saving, launching the game from the app, resume
after a restart, history with search and notes, statistics, event timeline,
per unit damage across a coupled set, the route map with event pins and the map
of the whole history, Discord Rich Presence, TrucksBook import, export and
backups.

Missing: replaying a drive along its route, an elevation profile, achievements
and whole session statistics. Details in [`docs/roadmap.md`](docs/roadmap.md).

## Licence

[MIT](LICENSE). The vendored SDK client and the RenCloud plugin are MIT as well,
so the whole project sits under one licence.

Waybill is not affiliated with TrucksBook, endorsed by it, or a continuation of
it. The name appears here only to refer to the service whose CSV exports the
import reads, and to explain what this project does differently. Euro Truck
Simulator 2 and American Truck Simulator are the property of SCS Software, who
are likewise not involved in this project.
