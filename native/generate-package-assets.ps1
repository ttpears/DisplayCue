param(
    [string]$Source = (Join-Path $PSScriptRoot '..\assets\monitor-hotkeys-source-v2.png'),
    [string]$Destination = (Join-Path $PSScriptRoot 'PackageAssets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

function Write-Logo([string]$Name, [int]$Width, [int]$Height, [int]$IconSize) {
    $canvas = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $sourceImage = [System.Drawing.Image]::FromFile($Source)
    $x = [int](($Width - $IconSize) / 2)
    $y = [int](($Height - $IconSize) / 2)
    $graphics.DrawImage($sourceImage, $x, $y, $IconSize, $IconSize)
    $canvas.Save((Join-Path $Destination $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $sourceImage.Dispose()
    $graphics.Dispose()
    $canvas.Dispose()
}

Write-Logo 'StoreLogo.png' 50 50 44
Write-Logo 'Square44x44Logo.png' 44 44 38
Write-Logo 'Square150x150Logo.png' 150 150 132
Write-Logo 'Wide310x150Logo.png' 310 150 132
