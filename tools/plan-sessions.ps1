# The sittings at the wheel, with one of them picked so the panel beside it has
# something in it.
#
#   powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 assets tools\plan-sessions.ps1
Click 60 138
Start-Sleep -Milliseconds 2200
Click 400 148
Start-Sleep -Milliseconds 900
Hover 640 740
Shot "sessions"
