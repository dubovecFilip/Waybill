# Assets

| File | Purpose |
|---|---|
| `logo.png` | Full resolution logo (700x700). Source for the application icon. |
| `logo.psd` | Layered source file for editing the logo. |
| `current-job.png` | The page the window opens on, waiting for a game. |
| `current-job-live.png` | The same page with a delivery under way. |
| `deliveries.png` | The delivery history, used at the top of the README. |
| `delivery-card.png` | One delivery on its own card. |
| `delivery-route.png` | The same card with the route, the height profile and the timeline slid out. |
| `delivery-route-wide.png` | The same again with the log column pulled wider by its handle. |
| `trailer-chain.png` | A triple with its units and dollies opened out. |
| `map.png` | The map page, with one route lit under the pointer. |
| `statistics.png` | The statistics page, with the period and game it is answering for. |
| `sessions.png` | The sittings at the wheel, one of them picked. |
| `waybill-sheet.png` | A delivery exported as three A4 sheets, side by side. |
| `legend.png` | Every mark the window uses, with what it means. |

Screenshots are the window's own area at a size of 1280x820, which is roughly its
default and wide enough for the delivery list to show most of its columns without
scrolling. What is kept is the client area only: no title bar, no border, and
nothing of whatever happened to be behind the window on the desktop. They are
rendered from the window itself rather than copied off the screen, so nothing
else can end up in the picture and the window does not have to be in front.

`waybill-sheet.png` is the exception: it is not a screenshot but a real export,
its three sheets downscaled from the 300 dpi originals to 1000 px tall and set
side by side, so each keeps A4's proportions rather than the window's. `legend.png` is the other: it is a dialog, so it is its
own width, and it was stretched tall enough to show every entry at once rather
than shown at its default size with half the list below the fold.

Each is named for what it shows rather than for where it happens to appear, so
moving one around the README does not leave the name lying.

They are captured from a real database rather than staged, which is why the
figures in them are ordinary rather than tidy.

`current-job.png` shows the page with no game running: the two launch buttons, the
log, and an empty map. `current-job-live.png` shows the other half, a delivery
under way, and it is taken with no game running either: `Waybill.exe --demo <id>`
puts a finished delivery on the live page, cut off five eighths of the way along.
Nothing in it is invented and nothing is drawn by anything but the page itself,
which is the whole reason for doing it that way rather than assembling a picture
that looks about right.

The captures are driven by a script that will not click anything until it has
proved the window is the one listening, because a refused foreground request once
sent a whole run of clicks into somebody else's application. The pictures are
asked of the window rather than scraped off the screen, so nothing behind it can
end up in one and the window does not have to be in front.

There is no shot of the route drawing itself. It is a couple of seconds of
movement, and a still frame of it is a picture of half a route, which reads as a
bug rather than as a feature.

There is none of the map at full screen either. It fills whatever monitor it is
opened on, so the shot would be the size of that monitor rather than the size
every other picture here is, and it shows the same map as `map.png` with more
room. The feature is described in the README instead.

The application icon `src/Waybill/waybill.ico` is generated from `logo.png` and
contains the 16, 32, 48, 64, 128 and 256 px sizes, so Windows can pick whichever
fits where it is drawing the icon (taskbar, desktop, alt+tab).

When the logo changes, the icon has to be regenerated. Any PNG to ICO converter
will do, as long as the result carries all of the sizes listed above.
