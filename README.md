# DisplayCue

DisplayCue is a lightweight Windows tray application for switching display layouts and physical monitor inputs.

Field notes: [Switching two monitors between two Windows PCs](https://hackyourworld.com/displaycue-two-pc-monitor-input-switching/) explains the DDC/CI handoff, rollback timer, and paired-PC coordination behind the current design.

## Features

- Save any number of named display profiles, from one display to an entire desk.
- Capture display positions, primary display, resolution, orientation and refresh rate.
- Assign any two profiles—or **all displays**—to configurable global hotkeys.
- Identify active monitors with large numbered overlays.
- Control HDMI and DisplayPort inputs on monitors that advertise DDC/CI input-source support.
- Coordinate profiles with a paired Windows PC before switching local monitor inputs.
- Verify monitor inputs before releasing the outgoing Windows displays.
- Confirm new configurations within 15 seconds; repeat validated configurations without the countdown.
- Undo a committed change for five minutes and test its recovery route.
- Recognize Home and Office desks, with a per-session desktop override and desktop-side return after undocking.
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

Arrange an extended desktop in Windows, then choose **Capture current layout** and **Save profile** to retain its complete layout. Existing profiles without a captured layout still select active displays; capture a layout before enabling automation. A change to inputs, layout or pairing configuration requires revalidation.

## Paired computers

Install DisplayCue on both PCs. Under **Paired computer**, generate a pairing key on one PC and paste the same key on the other. Enter the other computer's hostname or private IP address, enable peer control, allow DisplayCue on private networks if Windows Firewall asks, and use **Test connection**.

Each display profile can name a profile to run on the paired computer. Both sides capture their layouts and monitor inputs, prepare available destination connections while retaining the source connections, switch and read back the physical inputs, and finally apply and verify both Windows layouts. This preserves the DDC route until the physical handoff is complete. Failures attempt recovery on both computers, even if one recovery operation fails. Both PCs must run version 1.5 or later (handoff protocol 2).

DDC input changes are also coordinated. Each side reports which monitors it can currently control; if the preferred PC loses DDC access during a handoff, the same change is retried through the other PC. DisplayCue matches physical monitors by EDID fingerprint. If identical monitors cannot be distinguished automatically, enter the same short **Peer ID** (such as `left` or `right`) for that monitor in both PCs' monitor-input settings.

Requests are authenticated with HMAC-SHA256, expire after 30 seconds, and include replay-resistant nonces and per-handoff transaction IDs. The pairing key is encrypted for the current Windows user with DPAPI and is never written to `settings.json`.

Connection tests report the paired device, available profile names and active displays. Peer requests, transition phases and actionable failures are recorded in `%LocalAppData%\DisplayCue\peer.log`; pairing keys and monitor device paths are never written to that log.

## Automatic desks

Apply a profile manually and choose **Keep**, then select **Undo last change / test recovery** from the tray. A desk can be automated only after that configuration has a captured layout and both steps succeed. Paired takeovers also verify the peer's current configuration before committing unattended.

Open **Automatic desks**, select the profile, and use **Recognize this desk** with its monitors connected. Monitor identities distinguish desks; a saved Lenovo USB dock connection can recognize a desk before its video paths appear. Three stable observations debounce connection changes. Ambiguous desks are left for manual selection.

For automatic home return, select a dock connection that disappears when you unplug the laptop, and enter a tested desktop-local return profile. After a committed takeover, the laptop renews a short connection lease while that dock remains attached. The desktop restores its own profile after undocking or 30 seconds without a heartbeat, including when the laptop disappears from the network. Network outages can therefore return the screens to the desktop. Manual profile changes cancel the old return lease.

**Leave monitors on desktop this session** returns the previous state when Undo is available, or requests the configured desktop return. Automatic takeover stays suppressed until the dock is disconnected. Automation is off until explicitly enabled; both apps must be running.

See [the Home/Office setup guide](docs/HOME-OFFICE-SETUP.md) for mixed HDMI/DisplayPort connections and a laptop + two G5s / desktop + TV workflow.

## Safety and recovery

Before applying a profile, DisplayCue captures the active extended layout and reads the current input of every affected DDC/CI monitor. It refuses input switching when no participant can capture a previous input. If a new configuration's confirmation countdown expires or **Revert** is selected, both layers are restored. A committed change remains available through **Undo** for five minutes, until another transition replaces it.

The confirmation dialog is placed on the newly active primary display. After you choose **Keep**, application windows that are entirely outside the remaining desktop are brought back into view. DisplayCue also verifies that Windows actually activated exactly the displays saved in the profile.

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

Run regression tests without changing monitor settings:

```powershell
dotnet run --project tests/DisplayCue.Tests.csproj -c Release -r win-x64
```

Add `-- --read-only-hardware` to check native mode capture and USB enumeration on a connected Windows machine. Tests use an in-memory pairing key for authenticated loopback requests.

See [native/README.md](native/README.md) for implementation details and [docs/DESK-HANDOFF.md](docs/DESK-HANDOFF.md) for the coordination architecture.

## Privacy

See [PRIVACY.md](PRIVACY.md).
