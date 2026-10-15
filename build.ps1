param([switch]$Test, [string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.8 is required.' }
$outputDir = Join-Path $projectRoot $OutputDirectory
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$compileDir = Join-Path ([System.IO.Path]::GetTempPath()) ('parkcapture-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $compileDir | Out-Null
try {
    # The legacy native resource compiler cannot reliably write to Korean paths.
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.manifest') -Destination $compileDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\parkcapture.ico') -Destination $compileDir
    $updateReferences = @('/reference:System.Web.Extensions.dll', '/reference:System.Net.Http.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')
    $updateKey = "/resource:$projectRoot\assets\update-public-key.xml,ParkCapture.UpdateKey.xml"
    & $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$compileDir\ParkCapture.exe" "/win32manifest:$compileDir\app.manifest" "/win32icon:$compileDir\parkcapture.ico" "/resource:$projectRoot\assets\parkjuim-logo.png,ParkCapture.Logo.png" "/resource:$projectRoot\assets\parkcapture.ico,ParkCapture.App.ico" $updateKey /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:Microsoft.VisualBasic.dll $updateReferences $sourceFiles
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Copy-Item -LiteralPath (Join-Path $compileDir 'ParkCapture.exe') -Destination $outputDir -Force
    & $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$compileDir\ParkCapture.Updater.exe" "/win32manifest:$compileDir\app.manifest" $updateKey /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll $updateReferences (Join-Path $projectRoot 'updater\Program.cs') (Join-Path $projectRoot 'src\UpdatePackage.cs') (Join-Path $projectRoot 'src\UpdateInstaller.cs') (Join-Path $projectRoot 'src\AssemblyInfo.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Updater build failed.' }
    Copy-Item -LiteralPath (Join-Path $compileDir 'ParkCapture.Updater.exe') -Destination $outputDir -Force
}
finally {
    Get-ChildItem -LiteralPath $compileDir -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    Remove-Item -LiteralPath $compileDir
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'ParkCapture.exe.config') -Destination $outputDir -Force
Write-Output "Built: $outputDir\ParkCapture.exe"
if ($Test) {
    $reportPath = Join-Path $outputDir 'self-test.txt'
    $testStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $testStartInfo.FileName = Join-Path $outputDir 'ParkCapture.exe'
    $testStartInfo.Arguments = '--self-test "' + $reportPath + '"'
    $testStartInfo.UseShellExecute = $false
    $testStartInfo.CreateNoWindow = $true
    $testProcess = [System.Diagnostics.Process]::Start($testStartInfo)
    $testProcess.WaitForExit()
    if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Encoding UTF8 }
    if ($testProcess.ExitCode -ne 0) { throw 'Self tests failed.' }
}
