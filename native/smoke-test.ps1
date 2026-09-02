$ErrorActionPreference = 'Stop'
$assembly = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\DisplayCue.dll'
if (-not (Test-Path $assembly)) { throw 'Build DisplayCue in Release mode before running the smoke test.' }
Add-Type -Path $assembly
$monitors = [MonitorHotkeys.DisplayEngine]::GetMonitors()
$config = [MonitorHotkeys.ConfigStore]::Load()
$monitors | Select-Object Name,Active,DevicePath,GdiName | Format-Table -AutoSize
[pscustomobject]@{
  QuickAction1 = $config.QuickOneProfileId
  QuickAction1Hotkey = $config.QuickOneHotkey
  QuickAction2 = $config.QuickTwoProfileId
  QuickAction2Hotkey = $config.QuickTwoHotkey
  Profiles = $config.Profiles.Count
}
