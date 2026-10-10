# DisplayCue native app

This directory contains the compiled Windows application that replaces the PowerShell prototype.

## Architecture

- `DisplayEngine` wraps Windows DisplayConfig APIs, uses correctly sized native mode unions, and captures/restores extended layouts by device path.
- `DdcEngine` uses the native Windows monitor configuration API (`Dxva2.dll`) to discover DDC/CI displays, parse their MCCS capability strings, read VCP `0x60`, and switch only input values explicitly advertised by the monitor.
- `AppController` owns the notification-area icon, menus, global hotkeys and application lifetime.
- `PeerService` transports authenticated local-network state and transaction phases. Pairing keys are encrypted for the current Windows user with DPAPI.
- `SettingsForm` manages arbitrary display profiles and two assignable quick-action hotkeys. Each profile may include physical monitor input assignments.
- `HandoffSequence` coordinates snapshot, preparation, input switching/readback, final layout, confirmation and recovery through native/remote participants. Input routes remain active until switching is verified.
- `DeskDetection` matches saved external-monitor sets and optional present Lenovo USB device instances. Three stable observations debounce changes; a session override persists until disconnect.
- A desktop return lease is armed only after a committed paired handoff. A docked laptop renews it; the desktop applies its tested local return profile after disconnect or 30 seconds without renewal.
- Confirmation is required for new signatures. Captured layouts that pass Keep and Undo become eligible for automation. Pairing, layout and input changes invalidate that eligibility.
- Hardware operations run away from the UI thread. Inbound transactions serialize their phases and recover after two minutes without progress. Recovery continues with the other participant when one action fails.
- Configuration is stored under `%LocalAppData%\DisplayCue`. The app imports earlier DisplayCue and prototype settings on first launch.

## Local build

`build.ps1` publishes the .NET 8 self-contained Windows app into `outputs/DisplayCue.exe`. The .NET 8 SDK is required to build; the published app does not require a separate runtime installation.

## Release build

The SDK project targets Windows and publishes a self-contained single-file executable:

```powershell
dotnet publish MonitorHotkeys.csproj -c Release -r win-x64 --self-contained -o publish
```

GitHub Actions performs the same publish step and creates a release ZIP for version tags. Production releases should be Authenticode-signed before distribution.

## Verification

```powershell
dotnet run --project ../tests/DisplayCue.Tests.csproj -c Release -r win-x64
```

The simulator covers mixed inputs, source route retention, cancellation, partial failures, incorrect DDC readback, missing original inputs and peer loss. Tests also check native ABI sizes, saved-layout drift, legacy JSON, desk matching, calibration signatures, return leases, and authenticated protocol-2 loopback messages. Loopback keys stay in memory.

`smoke-test.ps1` adds read-only capture of this machine's display modes and Lenovo USB connections. Neither test entry point changes monitor settings or pairing keys. The Home/Office setup guide describes the remaining physical acceptance tests.

## Microsoft Store package

`build-store-package.ps1` creates both an unsigned `.msix` and the recommended `.msixupload` file. The manifest uses the Store identity reserved for DisplayCue (`Webmodule.DisplayCue`). The Microsoft Store signs the package after certification; no commercial certificate is required for Store distribution.

## Launch options

- No arguments: start in the notification area.
- `--settings`: start and immediately open Settings.
- `--ddc-report <path>`: write a read-only DDC/CI capability report and exit. If the path is omitted, the report is written under `%LocalAppData%\DisplayCue`.

## DDC/CI behavior

DisplayCue does not require PowerToys or a monitor vendor utility. Input control is implemented directly through the Windows physical-monitor APIs. Only active Windows display targets can initially be discovered. Some monitors continue answering DDC commands through an inactive video input and some do not; that property must be calibrated before relying on a single computer as a permanent controller.

Input mappings retain the local Windows device path, a cross-PC EDID fingerprint, an optional user-defined Peer ID, and the VCP value. The UI shows friendly names such as HDMI 1 and DisplayPort 1. During a paired handoff, a failed DDC operation is retried through the other PC; the same fallback is available during rollback. Unsupported monitors remain part of Windows display profiles but are left unchanged at the hardware-input layer.
