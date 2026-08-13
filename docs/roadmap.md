# TrucksBook Replacement — Project Vision & Roadmap

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

## Phase 4 — Cloud sync

- Accounts
- Backup sync
- Multi-PC support

## Phase 5 — VTC

- Shared feed
- Leaderboards
- Company statistics
- Management tools

---

# Product statement

**A local-first ETS2/ATS delivery tracker that automatically records every trip and provides rich driving analytics without invalidating deliveries.**
