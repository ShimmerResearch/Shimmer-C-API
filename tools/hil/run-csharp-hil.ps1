<#
.SYNOPSIS
Hardware-in-the-loop tests of the C# API on real Shimmer3 and Shimmer3R units, over Bluetooth Classic
and BLE. See tools/hil/README.md.

.DESCRIPTION
Builds tools/hil/ShimmerApiHil against the ShimmerAPI and Shimmer32FeetAPI in this checkout, runs
every item on every unit and transport the bench file lists, and prints pass, fail or not_covered
per item. The report, and a log per run, go to tools/hil/out/<time>.

-Report is the release gate's run. It needs every item on every unit and transport, at least one
Shimmer3 and one Shimmer3R, both transports, a clean working tree, and a commit that is on GitHub.
It then posts the commit status "csharp-hil" on that commit. PublishNuGet refuses to publish a
commit without a passing one, unless it is given a Jira key as a waiver.

.EXAMPLE
.\tools\hil\run-csharp-hil.ps1
.\tools\hil\run-csharp-hil.ps1 -Item stream.rest -Unit s3r-a -Transport ble
.\tools\hil\run-csharp-hil.ps1 -Report
#>
param(
    # The bench file, in the firmware HIL's rig.toml format. Default: $env:HIL_RIG.
    [string]$Rig = $env:HIL_RIG,
    # Only these items (see -List), units (by their rig name) or transports (classic, ble).
    [string[]]$Item,
    [string[]]$Unit,
    [string[]]$Transport,
    # List the items and stop.
    [switch]$List,
    # Run as the release gate and post the commit status "csharp-hil".
    [switch]$Report,
    # Use the last build instead of building first.
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $PSScriptRoot 'ShimmerApiHil\ShimmerApiHil.csproj'
$exe = Join-Path $PSScriptRoot 'ShimmerApiHil\bin\Release\net48\ShimmerApiHil.exe'
$context = 'csharp-hil'

function Fail([string]$message) {
    Write-Host $message -ForegroundColor Red
    exit 2
}

# --- Build -------------------------------------------------------------------------------------
if (-not $SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { Fail 'Visual Studio (with MSBuild) is needed to build the suite: vswhere.exe was not found.' }
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { Fail 'No MSBuild found by vswhere.' }
    Write-Host "Building $project"
    # Shimmer32FeetAPI packs a .nupkg on every build; not wanted here
    & $msbuild $project -t:Restore -v:quiet -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'Restore failed.' }
    & $msbuild $project -p:Configuration=Release -p:GeneratePackageOnBuild=false -v:minimal -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }
}
if (-not (Test-Path $exe)) { Fail "No build at $exe; run without -SkipBuild." }

if ($List) {
    & $exe --list
    exit $LASTEXITCODE
}

if (-not $Rig) { Fail 'No bench file: pass -Rig or set $env:HIL_RIG (see tools/hil/README.md).' }
if (-not (Test-Path $Rig)) { Fail "No bench file at $Rig." }
$Rig = (Resolve-Path $Rig).Path

# --- What -Report vouches for ------------------------------------------------------------------
$dirty = (git -C $repo status --porcelain) -join "`n"
$commit = ((git -C $repo rev-parse HEAD) -join '').Trim()
if ($Report) {
    if ($Item -or $Unit -or $Transport) {
        Fail '-Report needs every item on every unit and transport in the rig; drop -Item, -Unit and -Transport.'
    }
    if ($SkipBuild) { Fail '-Report tests a fresh build of the commit; drop -SkipBuild.' }
    $bench = (& $exe --rig $Rig --describe) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { Fail "Cannot read the bench file $Rig." }
    $platforms = @($bench.units | ForEach-Object { $_.platform } | Sort-Object -Unique)
    if (-not ($platforms -contains 'shimmer3' -and $platforms -contains 'shimmer3r')) {
        Fail "-Report needs at least one Shimmer3 and one Shimmer3R in the rig (found: $($platforms -join ', '))."
    }
    $transports = @($bench.units | ForEach-Object { $_.transports } | Sort-Object -Unique)
    if (-not ($transports -contains 'classic' -and $transports -contains 'ble')) {
        Fail "-Report needs both transports in the rig, Classic and BLE: they run different classes (found: $($transports -join ', '))."
    }
    if ($dirty) { Fail "-Report needs a clean working tree, so the tested code is exactly the commit:`n$dirty" }
    $origin = ((git -C $repo remote get-url origin) -join '').Trim()
    if ($origin -notmatch 'github\.com[:/](.+?)(\.git)?$') { Fail "origin is not a GitHub remote: $origin" }
    $githubRepo = $Matches[1]
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Fail '-Report posts the result with gh; install it and log in.' }
    gh api "repos/$githubRepo/commits/$commit" --silent 2>$null
    if ($LASTEXITCODE -ne 0) { Fail "Commit $commit is not on GitHub ($githubRepo). Push it first: the release checks the commit it builds." }
}

# --- Run ---------------------------------------------------------------------------------------
$outDir = Join-Path $PSScriptRoot ('out\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$label = if ($dirty) { "$commit+dirty" } else { $commit }
$arguments = @('--rig', $Rig, '--out', $outDir, '--commit', $label)
foreach ($i in @($Item)) { if ($i) { $arguments += @('--item', $i) } }
foreach ($u in @($Unit)) { if ($u) { $arguments += @('--unit', $u) } }
foreach ($t in @($Transport)) { if ($t) { $arguments += @('--transport', $t) } }

& $exe @arguments
$code = $LASTEXITCODE
if ($code -eq 2) { Fail 'The suite could not run (see above).' }
$reportFile = Join-Path $outDir 'report.json'
if (-not (Test-Path $reportFile)) { Fail "The suite wrote no report ($reportFile)." }
$result = Get-Content $reportFile -Raw | ConvertFrom-Json
$results = @($result.results)
$passed = @($results | Where-Object { $_.status -eq 'pass' })

if ($Report) {
    # A not_covered item is not a pass: the release asks for a waiver instead
    $state = if ($results.Count -gt 0 -and $passed.Count -eq $results.Count) { 'success' } else { 'failure' }
    $tested = @($result.units | ForEach-Object {
        $what = if ($_.identity) { $_.identity } else { $_.platform }
        "$what ($($_.transports -join '+'))"
    }) | Sort-Object -Unique
    $description = "$($passed.Count)/$($results.Count) items passed on $($result.bench): $($tested -join ', ')"
    if ($description.Length -gt 140) { $description = $description.Substring(0, 137) + '...' }
    gh api -X POST "repos/$githubRepo/statuses/$commit" -f state=$state -f context=$context -f description=$description --silent
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Could not post the commit status; the release will not see this run.' -ForegroundColor Red
        exit 2
    }
    Write-Host "Posted '$context' = $state on $githubRepo@$($commit.Substring(0, 8)): $description"
}

if ($passed.Count -ne $results.Count) {
    Write-Host "$($results.Count - $passed.Count) of $($results.Count) items did not pass. Report and logs: $outDir" -ForegroundColor Red
    exit 1
}
Write-Host "All $($results.Count) items passed. Report and logs: $outDir" -ForegroundColor Green
exit 0
