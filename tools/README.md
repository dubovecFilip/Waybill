# tools

`screenshots.ps1` takes the pictures in [`assets/`](../assets/README.md). It starts
the app, drives the window, and saves the client area of it.

```powershell
powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 assets tools\plan-readme.ps1
powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 assets tools\plan-live.ps1 --demo 316
```

Two things it does that are worth knowing about.

It will not click anything until it has proved the window it started is the one
holding the foreground. Windows refuses the foreground to a process nobody clicked
on, and a run that assumes it got it sends a whole plan of clicks and drags into
whatever application happens to be in front instead.

It asks the window for its own pixels rather than copying them off the screen, so
nothing behind it can end up in a picture, and the window does not have to be in
front to be photographed.

The plan is a separate script, dot-sourced by the harness, because what is
photographed changes far more often than how. Coordinates in a plan are the client
area of a 1280x820 window and are read off the pictures the harness takes: when the
layout moves, take a probe shot and read the new ones off that.
