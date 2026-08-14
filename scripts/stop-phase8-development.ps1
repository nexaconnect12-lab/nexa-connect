[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stateFile = Join-Path $root '.runstate\phase8\processes.json'
if (-not (Test-Path -LiteralPath $stateFile)) {
    Write-Host 'No Phase 8 launcher state exists.'
    exit 0
}

$parsedState = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
$states = @($parsedState | ForEach-Object { $_ })
$stopped = 0
$remaining = [System.Collections.Generic.List[int]]::new()

foreach ($state in $states) {
    $rootProcess = Get-Process -Id $state.Id -ErrorAction SilentlyContinue
    if ($null -eq $rootProcess) { continue }
    $expectedStart = [DateTimeOffset]::Parse(
        [string]$state.StartTimeUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    $actualStart = [DateTimeOffset]$rootProcess.StartTime
    if ($actualStart.ToUniversalTime().Ticks -ne $expectedStart.ToUniversalTime().Ticks) {
        Write-Warning "PID $($state.Id) was reused; it was not stopped."
        continue
    }

    # The launcher executes the built service DLL directly, so this validated
    # PID is the service rather than a dotnet-run wrapper with child processes.
    Stop-Process -Id $state.Id -Force
    try { Wait-Process -Id $state.Id -Timeout 5 -ErrorAction Stop }
    catch {
        if (Get-Process -Id $state.Id -ErrorAction SilentlyContinue) { $remaining.Add([int]$state.Id) }
    }
    $stopped++
}

if ($remaining.Count -gt 0) {
    throw "Phase 8 cleanup is incomplete; retained $stateFile. Remaining service PIDs: $($remaining -join ', ')"
}
Remove-Item -LiteralPath $stateFile
Write-Host "Stopped $stopped validated Phase 8 service process(es), including the portal BFFs recorded by the launcher."
