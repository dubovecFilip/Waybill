# Assets

| File | Purpose |
|---|---|
| `logo.png` | Full resolution logo (700x700). Source for the application icon. |
| `logo.psd` | Layered source file for editing the logo. |
| `screenshot.png` | The deliveries page, used at the top of the README. |
| `screenshot-current-job.png` | The current job page. |
| `screenshot-statistics.png` | The statistics page. |

Screenshots are taken at 1200x760, which is roughly the window's default size and
wide enough for the delivery list to show most of its columns without scrolling.

The application icon `src/Waybill/waybill.ico` is generated from `logo.png` and
contains the 16, 32, 48, 64, 128 and 256 px sizes, so Windows can pick whichever
fits where it is drawing the icon (taskbar, desktop, alt+tab).

When the logo changes, the icon has to be regenerated. Any PNG to ICO converter
will do, as long as the result carries all of the sizes listed above.
