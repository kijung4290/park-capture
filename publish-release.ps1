param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
$releaseTag = "v$Version"
$releaseRepo = 'kijung4290/park-capture'
$releaseAssets = @((Join-Path $PSScriptRoot "dist\ParkCapture-$Version-portable.zip"), (Join-Path $PSScriptRoot 'dist\update.json'), (Join-Path $PSScriptRoot 'dist\update.json.sig'))
foreach ($releaseAsset in $releaseAssets) { if (-not (Test-Path -LiteralPath $releaseAsset)) { throw "Missing release asset: $releaseAsset" } }
$releaseDraft = gh release view $releaseTag --repo $releaseRepo --json isDraft --jq .isDraft 2>$null
$releaseExists = $LASTEXITCODE -eq 0
if ($releaseExists -and $releaseDraft -ne 'true') { throw 'This version is already published. Increase the app version.' }
if (-not $releaseExists) {
    $releaseCommit = git rev-parse HEAD
    gh release create $releaseTag --repo $releaseRepo --target $releaseCommit --draft --title "Park Capture $releaseTag" --notes-file (Join-Path $PSScriptRoot 'RELEASE_NOTES.md')
    if ($LASTEXITCODE -ne 0) { throw 'Could not create release draft.' }
}
gh release upload $releaseTag --repo $releaseRepo @releaseAssets --clobber
if ($LASTEXITCODE -ne 0) { throw 'Could not upload release files. Release remains a draft.' }
gh release edit $releaseTag --repo $releaseRepo --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Could not publish release.' }
