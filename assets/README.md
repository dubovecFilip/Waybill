# Assets

| File | Purpose |
|---|---|
| `logo.png` | Full resolution logo (700x700). Source for the application icon. |
| `logo.psd` | Layered source file for editing the logo. |
| `deliveries.png` | The delivery history, used at the top of the README. |
| `delivery-card.png` | One delivery on its own card. |
| `delivery-timeline.png` | The same card with the timeline slid out. |
| `trailer-chain.png` | A triple with its units and dollies opened out. |
| `statistics.png` | The statistics page. |

Screenshots are taken at 1180x760, which is roughly the window's default size and
wide enough for the delivery list to show most of its columns without scrolling.
Each is named for what it shows rather than for where it happens to appear, so
moving one around the README does not leave the name lying.

They are captured from a real database rather than staged, which is why the
figures in them are ordinary rather than tidy.

There is no shot of the *Current job* page. It only has anything to show while a
delivery is actually running, and one taken with the game closed says nothing but
"waiting for the game", so it is better left out than faked.

The application icon `src/Waybill/waybill.ico` is generated from `logo.png` and
contains the 16, 32, 48, 64, 128 and 256 px sizes, so Windows can pick whichever
fits where it is drawing the icon (taskbar, desktop, alt+tab).

When the logo changes, the icon has to be regenerated. Any PNG to ICO converter
will do, as long as the result carries all of the sizes listed above.
