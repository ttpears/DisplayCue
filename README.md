# DisplayCue

DisplayCue is a lightweight Windows tray application for switching display layouts and physical monitor inputs.

## Features

- Save any number of named display profiles, from one display to an entire desk.
- Assign any two profiles—or **all displays**—to configurable global hotkeys.
- Identify active monitors with large numbered overlays.
- Control HDMI and DisplayPort inputs on monitors that advertise DDC/CI input-source support.
- Coordinate profiles with a paired Windows PC before switching local monitor inputs.
- Confirm standalone local profile changes within 15 seconds; verified paired scenes commit automatically and roll both PCs back on failure.
- Run entirely on the local computer without accounts, analytics, telemetry, or cloud services.

DisplayCue supports a variable number and type of displays. A profile may contain one screen, several monitors, a projector, a TV, or every connected display.

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

## Paired computers

Install DisplayCue on both PCs. Under **Paired computer**, generate a pairing key on one PC and paste the same key on the other. Enter the other computer's hostname or private IP address, enable peer control, allow DisplayCue on private networks if Windows Firewall asks, and use **Test connection**.

Each display profile can name a profile to run on the paired computer. DisplayCue reads the live topology on both PCs and performs the pair as one coordinated transition: both sides prepare, the PC giving up screens releases its Windows desktop, monitor inputs switch, and both sides apply and verify their final profiles. The transition commits automatically only after both target topologies verify; a failure asks both PCs to restore the state they captured at the start. This ordering works in either handoff direction and avoids two computers fighting over the same screens.

DDC input changes are also coordinated. Each side reports which monitors it can currently control; if the preferred PC loses DDC access during a handoff, the same change is retried through the other PC. DisplayCue matches physical monitors by EDID fingerprint. If identical monitors cannot be distinguished automatically, enter the same short **Peer ID** (such as `left` or `right`) for that monitor in both PCs' monitor-input settings.

If neither PC can initially reach DDC, DisplayCue applies the verified destination video signals and retries from both sides as the monitors reconnect. An unresolved optional DDC command no longer destroys an otherwise valid handoff. Once both computers verify their exact target topology, the synchronized scene commits automatically and reports any remaining DDC limitation in a notification.

Requests are authenticated with HMAC-SHA256, expire after 30 seconds, and include replay-resistant nonces and per-handoff transaction IDs. The pairing key is encrypted for the current Windows user with DPAPI and is never written to `settings.json`.

Connection tests report the paired device, available profile names, and current active profile/topology. Peer requests, transition phases, synchronized state, and actionable failures are recorded in `%LocalAppData%\DisplayCue\peer.log`; pairing keys and monitor device paths are never written to that log.

## Safety and recovery

Before applying a profile, DisplayCue captures the active Windows topology and current input of every affected DDC/CI monitor. Standalone local changes retain the **Keep/Revert** countdown. Paired scenes use two-sided verification and automatically restore both machines when a required phase fails.

For standalone profiles, the confirmation dialog is placed on the newly active primary display. After a local profile is kept—or a paired scene verifies—application windows entirely outside the remaining desktop are brought back into view.

When a profile brings monitors back from another computer or input, DisplayCue waits for every expected display to reconnect before asking Windows to extend the desktop. It will fail safely instead of silently applying only part of the profile.

DisplayCue always commits the requested Windows topology, even when DDC switching makes unwanted monitors disappear before Windows has removed their desktop surfaces. This moves windows and the taskbar back onto the displays retained by the profile.

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
