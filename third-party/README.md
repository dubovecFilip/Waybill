# Third-party

## `rencloud-scs-telemetry-v1.12.1.zip`

Release build of the **SCS SDK telemetry plugin** by RenCloud, the piece that
exposes ETS2/ATS telemetry through shared memory. Without it the game publishes
nothing and this app has nothing to read.

- Source: https://github.com/RenCloud/scs-sdk-plugin
- Licence: MIT
- Kept here so the exact plugin version this app was built against stays with the
  project; newer releases usually work, but the field layout can change.

Install: copy `Win64/scs-telemetry.dll` into the game's plugin folder, e.g.

```
C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator\bin\win_x64\plugins\
```

Create the `plugins` folder if it isn't there.

## `src/Waybill/SCSSdkClient/`

The C# client from the same project, vendored into the source tree (it is not
published as a NuGet package). Also MIT, same repository as above.

It carries a couple of local fixes that are documented in the code:
sticky event flags firing spuriously on connect, the "SDK not active" guard only
firing once, and `Delivered.StartedBackup` throwing during deserialization.
