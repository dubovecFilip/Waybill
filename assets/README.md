# Assets

| File | Purpose |
|---|---|
| `logo.png` | Full resolution logo (700x700). Source for the application icon. |
| `logo.psd` | Layered source file for editing the logo. |
| `deliveries.png` | The delivery history, used at the top of the README. |
| `delivery-card.png` | One delivery on its own card. |
| `delivery-route.png` | The same card with the route and timeline slid out. |
| `trailer-chain.png` | A triple with its units and dollies opened out. |
| `map.png` | The map page, with one route lit under the pointer. |
| `statistics.png` | The statistics page. |
| `waybill-sheet.png` | A delivery exported as an A4 sheet. |
| `legend.png` | Every mark the window uses, with what it means. |

Screenshots are taken at 1180x760, which is roughly the window's default size and
wide enough for the delivery list to show most of its columns without scrolling.
`waybill-sheet.png` is the exception: it is not a screenshot but a real export,
downscaled from the 300 dpi original to 1000 px tall, so it keeps A4's proportions
rather than the window's. `legend.png` is the other: it is a dialog, so it is its
own width, and it was stretched tall enough to show every entry at once rather
than shown at its default size with half the list below the fold.
Each is named for what it shows rather than for where it happens to appear, so
moving one around the README does not leave the name lying.

They are captured from a real database rather than staged, which is why the
figures in them are ordinary rather than tidy.

There is no shot of the *Current job* page, and none of *Overview* either. Both
only have anything to show while a delivery is actually running, and one taken
with the game closed says nothing but "waiting for the game" across the part that
matters, so they are better left out than faked.

There is none of the route drawing itself. It is two and a half seconds of
movement, and a still frame of it is a picture of half a route, which looks like
a bug rather than like a feature.

There is none of the map at full screen either. It fills whatever monitor it is
opened on, so the shot would be the size of that monitor rather than the size
every other picture here is, and it shows the same map as `map.png` with more
room. The feature is described in the README instead.

The application icon `src/Waybill/waybill.ico` is generated from `logo.png` and
contains the 16, 32, 48, 64, 128 and 256 px sizes, so Windows can pick whichever
fits where it is drawing the icon (taskbar, desktop, alt+tab).

When the logo changes, the icon has to be regenerated. Any PNG to ICO converter
will do, as long as the result carries all of the sizes listed above.
