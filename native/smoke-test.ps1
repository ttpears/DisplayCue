$ErrorActionPreference = 'Stop'
Add-Type -Path @((Join-Path $PSScriptRoot 'DdcEngine.cs'), (Join-Path $PSScriptRoot 'MonitorHotkeys.cs')) `
  -ReferencedAssemblies @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xml.dll')
$monitors = [MonitorHotkeys.DisplayEngine]::GetMonitors()
$config = [MonitorHotkeys.ConfigStore]::Load()
$monitors | Select-Object Name,Active,DevicePath,GdiName | Format-Table -AutoSize
[pscustomobject]@{
  Tv = $config.TvMatch
  TvHotkey = $config.TvOnlyHotkey
  AllHotkey = $config.AllHotkey
  Profiles = $config.Profiles.Count
}
