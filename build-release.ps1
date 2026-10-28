param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\TimeManagementMaster\TimeManagementMaster.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile
$releaseVersion = [string]$projectXml.Project.PropertyGroup.Version
$releaseName = "TimeManagementMaster-$releaseVersion-win-x64"
$releaseRoot = Join-Path $projectRoot 'publish\releases'
$outputDirectory = Join-Path $releaseRoot $releaseName
$archivePath = Join-Path $releaseRoot "$releaseName.zip"

if (-not $SkipTests) {
    & dotnet test (Join-Path $projectRoot 'TimeManagementMaster.sln') -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
}

& dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $outputDirectory --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDirectory 'README.md') -Force
@"
Time Management Master $releaseVersion - Windows x64

Extract the entire ZIP into a folder and run TimeManagementMaster.exe.
The .NET runtime is included. Keep all extracted files together.

Employee data is saved separately in:
%LocalAppData%\TimeManagementMaster\data.sqlite
Backups: %LocalAppData%\TimeManagementMaster\backups\

This release does not contain an employee database.
"@ | Set-Content -LiteralPath (Join-Path $outputDirectory 'README.txt') -Encoding utf8

Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal -Force
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash *$releaseName.zip" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii
Write-Output "Release folder: $outputDirectory"
Write-Output "Release ZIP: $archivePath"
Write-Output "SHA256: $archiveHash"
