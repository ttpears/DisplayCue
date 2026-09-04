param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\store')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'MonitorHotkeys.csproj'
$publish = Join-Path $Output 'layout'
$assetOutput = Join-Path $publish 'Assets'
$packageVersion = '1.4.0.0'
$msix = Join-Path $Output "DisplayCue_${packageVersion}_x64.msix"

Remove-Item -LiteralPath $Output -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish -Force | Out-Null
dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-restore -o $publish

& (Join-Path $PSScriptRoot 'generate-package-assets.ps1') -Destination $assetOutput
Copy-Item (Join-Path $PSScriptRoot 'Package.appxmanifest') (Join-Path $publish 'AppxManifest.xml')

$windowsKitBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$toolRoots = @($windowsKitBin, (Join-Path (Split-Path -Parent $PSScriptRoot) 'work\windows-sdk-tools'))
$makeAppx = $toolRoots |
    Where-Object { Test-Path $_ } |
    ForEach-Object { Get-ChildItem $_ -Filter makeappx.exe -Recurse } |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $makeAppx) { throw 'MakeAppx.exe was not found. Install the Windows 10/11 SDK.' }

$pdb = Get-ChildItem $publish -Filter '*.pdb' -Recurse | Select-Object -First 1
$symbols = $null
if ($pdb) {
    $symbolStage = Join-Path $Output 'symbols'
    New-Item -ItemType Directory -Path $symbolStage -Force | Out-Null
    Copy-Item $pdb.FullName $symbolStage
    Remove-Item -LiteralPath $pdb.FullName -Force
    $symbolsZip = Join-Path $Output "DisplayCue_${packageVersion}_x64.zip"
    Compress-Archive -Path (Join-Path $symbolStage '*') -DestinationPath $symbolsZip
    $symbols = Join-Path $Output "DisplayCue_${packageVersion}_x64.appxsym"
    Move-Item $symbolsZip $symbols
}

& $makeAppx.FullName pack /d $publish /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }

$uploadStage = Join-Path $Output 'upload'
New-Item -ItemType Directory -Path $uploadStage -Force | Out-Null
Copy-Item $msix $uploadStage
if ($symbols) { Copy-Item $symbols $uploadStage }
$uploadZip = Join-Path $Output "DisplayCue_${packageVersion}_x64.zip"
Compress-Archive -Path (Join-Path $uploadStage '*') -DestinationPath $uploadZip
Move-Item $uploadZip (Join-Path $Output "DisplayCue_${packageVersion}_x64.msixupload")

Write-Host "Store package: $msix"
Write-Host "Partner Center upload: $(Join-Path $Output "DisplayCue_${packageVersion}_x64.msixupload")"
