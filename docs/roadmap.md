# Waybill: Project Vision & Roadmap

## Why am I creating a replacement?

The main reason is the implementation of a rule in TrucksBook that makes a delivery invalid if the lane assistant was activated during the delivery process.

I want software that:

- tracks my deliveries reliably,
- keeps all deliveries valid,
- and provides detailed statistics about my drives.

---

# What I like about TrucksBook

## Game integration

- Start ATS/ETS2 directly from the client.
- Keeps a history of all jobs.

## Personal logbook

Per game (ETS2 / ATS) with its own units:

- Accepted distance
- Profit
- XP
- Offences
- Fuel cost
- Cargo weight
- Real time taken
- and many other statistics

## Cargo creation

- Cargo type
- Job expiration
- Origin and destination
  - Country
  - City
  - Company
- Estimated distance
- Estimated weight
- Cargo preview (if possible)

## Future features from TrucksBook worth keeping

- Company / VTC management
- Shared map features

---

# Core philosophy of this project

A delivery should **never become invalid** because a driving assist was used.

Instead, assists and other events should be stored as **metadata** and shown in statistics.

Examples:

- Lane assist enabled
- Cruise control used
- Speeding
- Collision
- Fine received

---

# MVP (Version 1.0)

These are the features that should exist before any large roadmap additions.

## Automatic game detection

- Detect ATS/ETS2 start and stop.
- Optionally launch automatically with each game.

## Automatic delivery tracking

- Detect jobs through telemetry.
- Start and finish deliveries without manual input.

## Local-first storage

- SQLite database.
- Fully functional offline.

## Delivery history

- Search
- Filter
- Sort
- Edit notes
- Export CSV / JSON

## Statistics dashboard

- Distance driven
- Profit earned
- Fuel used
- Driving time
- Average speed
- Favorite truck
- Favorite route
- Favorite cargo

---

# Telemetry & Event System

Capture events during the trip, not only the final result.

| Event                  | Purpose            |
| ---------------------- | ------------------ |
| Collision              | Safety statistics  |
| Fine received          | Economy statistics |
| Lane assist enabled    | Informational      |
| Cruise control usage   | Driving style      |
| Speeding duration      | Driving habits     |
| Engine damage increase | Vehicle wear       |
| Refuel                 | Fuel analytics     |
| Sleep / rest           | Trip timeline      |
| Ferry / train usage    | Route composition  |

A delivery becomes a **timeline of events**.

---

# Features that differentiate this project

## Route recording

Store GPS coordinates periodically and provide:

- Route map
- Trip replay
- GPX export
- KML export

## Achievement system

Examples:

- Longest delivery without damage
- 10 countries in one session
- 1,000 km without speeding
- Most profitable cargo type

## Session analytics

Track entire gaming sessions:

- Session start/end
- Total distance
- Total profit
- Number of deliveries
- Average speed
- Breaks

---

# Data model (future-proof)

## Deliveries

- job_id
- game
- profile_name
- truck_id
- trailer_id
- cargo_id
- origin_company
- destination_company
- planned_distance_km
- actual_distance_km
- income
- expenses
- fuel_used_l
- started_at
- finished_at
- cancelled
- damage_percent
- telemetry_version

## Trip points

- delivery_id
- timestamp
- x
- y
- z
- speed
- heading

## Events

- delivery_id
- timestamp
- event_type
- value
- extra_json

Store coordinates from day one even if maps are implemented later.

---

# User experience features

## One-click play

Launch:

- telemetry plugin
- tracker client
- ETS2 / ATS

with a single button.

## Offline mode

Everything works without an account.

## Import from TrucksBook

Allow importing historical deliveries through CSV or API.

## Backup & restore

One-click backup of the local database.

---

# VTC roadmap

## First release

- Member list
- Shared delivery feed
- Company totals
- Leaderboards

## Later releases

- Payroll
- Convoy scheduling
- Permissions
- Recruitment workflow

---

# Security & anti-cheat

Goals:

- Sign telemetry sessions locally.
- Detect impossible values.
- Mark suspicious deliveries as **Unverified**.
- Let communities decide verification requirements.

---

# Features to postpone

To avoid scope creep:

- DLC ownership highlighting
- Cargo image previews
- Custom cargo marketplace
- Real-time VTC map
- TruckersMP integration
- Advanced company management

---

# Analytics improvements

Add comparison dashboards:

- This week vs last week
- This month vs last month
- ETS2 vs ATS
- Truck A vs Truck B

---

# The three strongest selling points

1. Deliveries are never invalidated.
2. Tracking starts automatically.
3. Actual routes and trip replay are available.

---

# Recommended roadmap

## Phase 1 — Core tracker

- Auto-start
- Telemetry capture
- Delivery history
- Local database
- Basic logbook
- Event tracking

## Phase 2 — Analytics

- Achievements
- Session statistics
- Comparisons
- Export tools

## Phase 3 — Maps

- Route recording
- Map view
- Replay
- Filters

Licensing note: the fullest map projects are GPL 3.0, including TruckNav-Sim and
truckermudgeon/maps, which would force GPL on all of Waybill. Waybill is MIT, so
route drawing was written from scratch instead.

For city positions there is one permissive option worth knowing about:
dariowouters/ts-map is MIT, is written in C#, and reads the player's own installed
game rather than shipping extracted data. That is the right shape for this
project, since the result then matches the player's own DLC and nothing derived
from SCS's files ends up in this repository. Two older sources are not usable:
thezir.com is from 2017 with all rights reserved, and Koenvh1's premade files are
MIT but their ATS data stops at game version 1.5, covering California and Nevada
only.

## Phase 4 — Cloud sync

- Accounts
- Backup sync
- Multi-PC support

## Phase 5 — VTC

- Shared feed
- Leaderboards
- Company statistics

---

# Where it stands

This document is the vision it was written as. What follows is what has actually
been built, so the two can be told apart.

**Phase 1 is done.** Auto start, telemetry capture, delivery history, local
database, logbook and event tracking all work, and have been driven for real
rather than only tested.

**Beyond it, already built:**

- Every verdict explained in words on the delivery's own card, rather than a label
- Per unit damage across doubles and triples, with dollies told apart from trailers
- The route of each delivery drawn, with its fines and collisions marked on it
- A map of the whole history, built from the driver's own drives and nothing else
- A delivery written out as a printable A4 waybill, which no plan here asked for
  and which turned out to be the right home for the paper idea: as a skin for the
  window it would have cost the map its zooming, and a fixed sheet cannot hold
  seven trailer units, while a file is a fixed size by definition and can run onto
  a second sheet
- Which market a job came from, and whether the trailer was the driver's own
- Driving between jobs kept as well, counted apart from the deliveries and drawn
  in a quieter line, so the map fills in without the totals being flattered
- One page for the drive in progress, which is the only one that answers what is
  going on rather than a question the reader has to arrive with, and which turns the
  map to whatever angle fills the shape of the panel, so a route running north to
  south fills a tall panel standing up and a wide one lying down
- A delivery's route drawn the way it was driven rather than all at once, which
  is route replay from Phase 3 in the only form the data supports: from above,
  along the line, at the pace the drive actually went
- A legend, because most of what the window says it says by drawing: the verdict
  dots, the signs on the timeline, the colour of a route, the stripes on an
  oversize load. None of that was in the plan, and all of it needed explaining
- Discord Rich Presence, over the local pipe, no account and nothing sent anywhere
- Export to CSV and JSON, backup and restore, TrucksBook import
- Rebuild from the raw recordings, which is what makes every judgement above
  revisable rather than frozen at whatever the tracker believed that day
- The sheet grown into a document of three: the consignment and the route on the
  front, the equipment, the running costs and a speed trace on the second, the log,
  the driver's own note and the stamp on the back. It saves as pictures or as one
  PDF, the route is drawn over every road already driven with the towns named, and
  the driver signs it in their own hand
- The condition of the truck, each unit and the load before and after, rather than
  only what the delivery added
- Driving style judged against the length of the run, since three fines crossing
  town and three across fifteen hundred kilometres are not the same driver
- The state or the country beside a city, from a table, because neither game
  reports it
- The window in English, Slovak, Czech, German and Spanish
- Money converted rather than relabelled when one system is forced, so a column
  holding both games adds up to something
- A finished delivery shown on the live page as though it were under way, which is
  the only way to look at that page, or photograph it, without a game running
- A strip at the foot of the sidebar saying the last five things Waybill noticed,
  beside every page rather than on one, each line marked for what it was: a load
  taken on, a load handed over, an award earned
- Electric tractors told from diesel ones by their identifier, counted in kilowatt
  hours everywhere a tank would be counted in litres, and kept apart in the totals
- A sleep told from a charging stop, a repair or a job taken from a menu, since the
  game jumps its clock for all four and only one of them is rest
- Every heading in the window kept to a word or two, with the sentence it could not
  hold under the pointer instead

**Phase 2 is done.** Export tools are there, and so are period comparisons: the
statistics page answers for a week or a month and says how each figure moved
against the same length of time before it, and it can be narrowed to one game.
Session statistics are built: a page of sittings at the wheel, each with what was
driven in it, where a sitting is one run of the app and runs close together are
the same sitting. One truck against another is built, as a page of its own: a row per tractor over
the whole history, since a week's window hides the difference between a truck that
has pulled twenty-six loads and one that has pulled one. Achievements are
built, as a page of their own: seventy-two of them on four shelves, worth
Waybill's own experience and a level to show for it. Some repeat, and then doing
the thing again counts again. Distance is kept apart by game and never converted,
so Europe counts in kilometres and America in miles. They are worked out from the
deliveries Waybill watched itself, since an imported row carries a distance and a
payout and nothing else, and they are backfilled quietly, so the history that
already exists counts without announcing forty awards at once.

What this phase asked for that is still open is fuel and cargo weight as totals in
the logbook. Every refuel is stored with what it cost and every delivery with what
it carried; neither is summed anywhere yet.

**Phase 3, mostly.** The map is drawn: a delivery's own route with its events
marked on it, and a page showing every drive of one game at once, where clicking a
route opens it. Replay is there in the form the data supports, which is the line
drawing itself from the pickup to the drop at the pace the drive went. The
elevation profile is there too, drawn beside the route and in step with it, and
it turned out to need the same decision the map did: the game's vertical is no
more a scaled elevation than its horizontal is a scaled country, so the profile
shows the shape and reports no metres. Replay from inside the cab is not built.

The map turned out to need a decision this document did not anticipate. The game's
world is not a scaled country: measured over nineteen deliveries some pairs of
cities sit thirteen times closer than reality and others thirty, so no projection
lines it up with real geography, and the closest fit leaves cities about thirty
kilometres out. Rather than draw borders in the wrong place, the map is built
entirely from the driver's own data. The routes already driven are the background
and the cities are learned from the jobs, which means the picture fills in as more
gets driven. GPX and KML export are still open, and would need that same decision
faced again, since both formats want real coordinates.

**Phases 4 and 5 have not started**, and should not until the local side is worth
sharing. The one piece of groundwork that exists is that a delivery now has a
stable identity, so the same drive can be referred to across machines without
inventing a server first.

**What this document asked for that the telemetry cannot give.** Worth writing
down, because each of these reads like an oversight and none of them is.

- *Lane assist enabled.* The plan names it twice, once as the reason this project
  exists and once as an event to record. The SDK has no field for it: the game does
  not report whether the assist is on. What the project promised is kept anyway,
  since keeping every delivery valid needs no such field, but the assist itself
  cannot be shown in the statistics because nothing outside the game knows it.
- *XP.* Reported after all, on the job delivered event, and stored since. This
  document said otherwise for a while on the strength of a field that reads zero
  until the job is handed over.
- *Profile name.* Not reported. A delivery knows its game, its truck and its
  trailer, and nothing about which profile drove it.
- *Cargo previews.* There is no image in the telemetry, only names and
  identifiers. Already on the postponed list, and this is why it stays there.
- *Replay from inside the cab.* One position a second and no wheel angles or
  camera is a line on a map, which is what it was made into. A drive played back
  through the windscreen would need the recording to be something else entirely.

**Small things still open, in the order they are worth doing.**

- Fuel and cargo weight as totals in the logbook: every refuel is stored with what
  it cost and every delivery with what it carried, but neither is summed anywhere
- GPX and KML, which need the coordinate decision above faced again, since both
  formats want real ones
- Heading in the recording. It is read live now, for the needle on the map of the
  drive in progress, but the field it comes from is internal to the SDK and never
  written to a recording, so a finished drive still has no facing to it

---

# Product statement

**A local-first ETS2/ATS delivery tracker that automatically records every trip and provides rich driving analytics without invalidating deliveries.**
