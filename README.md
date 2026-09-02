# DisplayCue

DisplayCue is a lightweight Windows tray application for switching display layouts and physical monitor inputs.

## Features

- Switch between **TV only**, **all displays**, or any number of named display profiles.
- Configure global hotkeys for the two most-used actions.
- Identify active monitors with large numbered overlays.
- Control HDMI and DisplayPort inputs on monitors that advertise DDC/CI input-source support.
- Restore the previous Windows topology and monitor inputs automatically unless a new profile is confirmed within 15 seconds.
- Run entirely on the local computer without accounts, analytics, telemetry, or cloud services.

DisplayCue supports a variable number of displays. A profile may contain one monitor, one TV, several monitors, or every connected display.

## Install

### Microsoft Store

The Store release is the recommended installation because Microsoft signs and updates it.

### GitHub release

Download `DisplayCue-win-x64.zip` from the latest GitHub Release, extract it, and run `DisplayCue.exe`. Windows may warn about a GitHub build because it is not signed by the Microsoft Store.

Every push and pull request also produces a downloadable Actions artifact for testing. Version tags beginning with `v` publish the same files as a GitHub Release.

## Using profiles

Right-click the DisplayCue notification-area icon and choose **Settings and profiles**. A profile selects the displays Windows should keep active. Select **Monitor inputs** to optionally assign HDMI or DisplayPort inputs to DDC/CI-capable monitors.

Only values advertised by each monitor are offered. Unsupported monitors remain available for Windows display profiles and are otherwise left unchanged.

The two quick actions can have global hotkeys. Additional profiles remain available from the tray menu so DisplayCue does not reserve a large set of system-wide shortcuts.

## Safety and recovery

Before applying a profile, DisplayCue captures the active Windows topology and current input of every affected DDC/CI monitor. If the confirmation countdown expires or **Revert** is selected, both layers are restored.

DDC/CI implementations vary by monitor, graphics adapter, dock, and cable. Test a physical input mapping while the monitor controls remain accessible. A read-only compatibility report can be generated with:

```powershell
DisplayCue.exe --ddc-report ddc-report.txt
```

The report contains monitor identifiers and capabilities but no application settings or user data.

## Build

Requirements: Windows and the .NET 8 SDK.

```powershell
dotnet publish native/MonitorHotkeys.csproj -c Release -r win-x64 --self-contained -o artifacts/DisplayCue
```

`native/build-store-package.ps1` produces the MSIX and Partner Center `.msixupload`. Microsoft signs certified Store packages; the unsigned local MSIX is intended for packaging validation.

See [native/README.md](native/README.md) for implementation details and [docs/DESK-HANDOFF.md](docs/DESK-HANDOFF.md) for the planned secure multi-computer coordination architecture.

## Privacy

See [PRIVACY.md](PRIVACY.md).
