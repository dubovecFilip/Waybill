# How Waybill measures distance and speed

The game works in two unit systems and they must never be mixed. Swapping one
for the other is the most common source of nonsense numbers, so here is what each
one means.

## The two systems

| Measurement | One real drive | System |
|---|---|---|
| Odometer | 176.60 km | simulated km |
| Speed times **game** time | 175.62 km | simulated km |
| `JobDelivered.DistanceKm` from the game | 176 km | simulated km |
| World position | 13.08 km | world space |
| Speed times **real** time | 12.94 km | world space |

The map is scaled down, by about 13.5x on the routes measured here, and the game
clock runs roughly 13x faster than real time. The first three rows agree with
each other and so do the last two, but between the groups sits that compression
factor.

A delivery is reported in simulated km, the same figure the game and the job
offer show, so the odometer leads. Its increments between ticks are summed, with
negative jumps and jumps above a threshold rejected, since those come from a
teleport or from a position being loaded.

World position is stored separately. It serves teleport detection and, later on,
drawing the route. It is never used to count distance travelled.

## Average speed

It has to divide simulated km by the game time spent driving.

* Dividing by real time reports the time compression as speed, around 770 km/h.
* Dividing by total game time counts sleep and pauses too, which understates it.

A separate counter of game minutes therefore runs only while driving. Imported
deliveries do not have one, as there is no telemetry behind them, so they get no
average speed.

## Units in storage

The database always stores metric and converts only for display. The history
therefore does not depend on which setting was active during the drive, and
switching units redraws old deliveries too.

## Game time

The game clock has a resolution of one minute, so short intervals are coarse.
That is still enough to tell a pause from a client outage: if fewer game minutes
passed between two ticks than the real time between them would imply, the game
was paused, otherwise Waybill was not running.
