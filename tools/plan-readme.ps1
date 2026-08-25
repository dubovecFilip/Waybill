# What to photograph for the README, in one pass of the window.
#
#   powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 assets tools\plan-readme.ps1
#
# Coordinates are the client area of a 1280x820 window, which is what the harness
# sets. They are read off the pictures it takes, so when the layout moves, take a
# probe shot and read the new ones off that rather than guessing at an offset.
#
# The live page is not here: it needs the app started with --demo, so it has a plan
# of its own in plan-live.ps1.

# the page the window opens on, with no game running
Start-Sleep -Milliseconds 1200
Hover 640 700
Shot "current-job"

# the history
Click 60 100
Start-Sleep -Milliseconds 1400
Hover 640 700
Shot "deliveries"

# one delivery on its own card
Click 500 200
Enter
Start-Sleep -Milliseconds 900
Hover 640 700
Shot "delivery-card"

# the route, the profile and the log beside them
Click 900 73
Start-Sleep -Milliseconds 2600
Hover 640 700
Shot "delivery-route"

# and the log pulled wider by the handle down its left edge
Drag 765 420 560 420
Hover 640 700
Shot "delivery-route-wide"
Click 1178 73
Start-Sleep -Milliseconds 1200

# a triple, opened out unit by unit. Twenty-four rows down the list rather than
# searched for, since which delivery has five units is a fact about this history.
Click 500 128
Key 0x28 24
Enter
Start-Sleep -Milliseconds 1000
Click 1200 398
Start-Sleep -Milliseconds 900
Hover 640 700
Shot "trailer-chain"
Click 1178 73
Start-Sleep -Milliseconds 1200

# the statistics
Click 58 176
Start-Sleep -Milliseconds 2200
Hover 640 700
Shot "statistics"

# everywhere driven
Click 46 138
Start-Sleep -Milliseconds 3000
Hover 640 700
Shot "map"

# every mark, in a window of its own, stretched tall enough to hold all of them
Click 174 13
Start-Sleep -Milliseconds 700
Click 195 34
Start-Sleep -Milliseconds 2000
Dialog "legend" 780 1200
