$ErrorActionPreference = 'Stop'
dotnet run --project (Join-Path $PSScriptRoot '..\tests\DisplayCue.Tests.csproj') -c Release -r win-x64 -- --read-only-hardware
if ($LASTEXITCODE -ne 0) { throw 'DisplayCue regression or read-only hardware checks failed.' }
