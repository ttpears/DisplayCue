# DisplayCue native app

This directory contains the compiled Windows application that replaces the PowerShell prototype.

## Architecture

- `DisplayEngine` wraps Windows DisplayConfig APIs and identifies monitors by EDID device path.
- `DdcEngine` uses the native Windows monitor configuration API (`Dxva2.dll`) to discover DDC/CI displays, parse their MCCS capability strings, read VCP `0x60`, and switch only input values explicitly advertised by the monitor.
- `AppController` owns the notification-area icon, menus, global hotkeys and application lifetime.
- `SettingsForm` manages arbitrary display profiles and two assignable quick-action hotkeys. Each profile may include physical monitor input assignments.
- Profile changes capture the previous active topology and affected monitor inputs, then automatically roll both back after 15 seconds unless confirmed.
- Configuration is stored under `%LocalAppData%\DisplayCue`. The app imports earlier DisplayCue and prototype settings on first launch.

## Local legacy build

`build.ps1` uses the .NET Framework compiler already included with Windows and writes `outputs/DisplayCue.Native.exe`. This build is useful where unsigned locally compiled programs are permitted.

## Release build

The SDK project targets Windows and publishes a self-contained single-file executable:

```powershell
dotnet publish MonitorHotkeys.csproj -c Release -r win-x64 --self-contained -o publish
```

GitHub Actions performs the same publish step and creates a release ZIP for version tags. Production releases should be Authenticode-signed before distribution.

## Microsoft Store package

`build-store-package.ps1` creates both an unsigned `.msix` and the recommended `.msixupload` file. The manifest uses the Store identity reserved for DisplayCue (`Webmodule.DisplayCue`). The Microsoft Store signs the package after certification; no commercial certificate is required for Store distribution.

## Launch options

- No arguments: start in the notification area.
- `--settings`: start and immediately open Settings.
- `--ddc-report <path>`: write a read-only DDC/CI capability report and exit. If the path is omitted, the report is written under `%LocalAppData%\DisplayCue`.

## DDC/CI behavior

DisplayCue does not require PowerToys or a monitor vendor utility. Input control is implemented directly through the Windows physical-monitor APIs. Only active Windows display targets can initially be discovered. Some monitors continue answering DDC commands through an inactive video input and some do not; that property must be calibrated before relying on a single computer as a permanent controller.

Input mappings are stored by the Windows monitor device path and VCP value. The UI shows friendly names such as HDMI 1 and DisplayPort 1, while retaining the exact value reported by the monitor. Unsupported monitors remain part of Windows display profiles but are left unchanged at the hardware-input layer.
