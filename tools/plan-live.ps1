# The live page with a delivery on it. Wants the app started with --demo:
#
#   powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 assets tools\plan-live.ps1 --demo <id>
#
# Any delivery of your own will do; `Waybill.exe --list` prints the numbers. They
# are not stable across a rebuild, which renumbers every row, so take one from the
# list rather than from an old command line. Nothing is written and no telemetry is
# read; see MainForm.DemoDelivery.
Start-Sleep -Seconds 3
Hover 640 700
Shot "current-job-live"
