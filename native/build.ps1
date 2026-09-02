$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'outputs'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'DisplayCue.Native.exe'
if (Test-Path $exe) { Remove-Item -LiteralPath $exe }
Add-Type -Path (Join-Path $PSScriptRoot 'MonitorHotkeys.cs') `
  -ReferencedAssemblies @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xml.dll') `
  -OutputAssembly $exe -OutputType WindowsApplication
Copy-Item -LiteralPath (Join-Path $root 'assets\tray-icon-v3.ico') -Destination (Join-Path $output 'tray-icon.ico') -Force
Write-Output "Built $exe"
