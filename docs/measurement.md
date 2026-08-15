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

The game clock has a resolution of one minute, so short intervals are coarse. It
also runs faster than real time, by the factor the game publishes for itself in
`CommonValues.Scale`, which reads 20 on every recording measured.

That is what separates a pause from a stall in this app: the clock keeps running
while the app is stalled and stops while the game is paused. Both leave the same
hole in a recording.

The comparison has to be against that reported scale, not against real time. Over
a hole of ten or thirty seconds the one minute resolution means the clock reads
either 0 or 1 depending only on whether a minute boundary fell inside it, and a
reading of 1 is far above real time while being far below what running would have
produced. Compared against real time, nine holes across three sessions were called
stalls; every one of them had moved the clock by exactly one minute where running
would have moved it by ten to twenty, so every one was a pause.

A hole therefore counts as a stall only if the clock moved further than its own
one minute resolution and reached at least half of what the reported scale
predicts. Where the two cannot be told apart, it is read as a pause: being unable
to prove the app stalled is not evidence that it did.
