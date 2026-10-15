$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Test
$releaseDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$releaseVersion = (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\ParkCapture.exe')).VersionInfo.FileVersion -replace '\.0$', ''
$releaseZip = Join-Path $releaseDir "ParkCapture-$releaseVersion-portable.zip"
Compress-Archive -LiteralPath @((Join-Path $PSScriptRoot 'bin\ParkCapture.exe'), (Join-Path $PSScriptRoot 'bin\ParkCapture.exe.config'), (Join-Path $PSScriptRoot 'bin\ParkCapture.Updater.exe'), (Join-Path $PSScriptRoot 'README.md')) -DestinationPath $releaseZip -Force
& (Join-Path $PSScriptRoot 'sign-release.ps1') -Package $releaseZip -Version $releaseVersion
Write-Output "Portable package: $releaseZip"
