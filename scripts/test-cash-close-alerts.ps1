[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($docker) { $executable = $docker.Source }
else { $executable = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/DockerDesktop/resources/bin/docker.exe' }
if (-not (Get-Command $executable -ErrorAction SilentlyContinue)) { throw 'Docker is required for Prometheus rule verification.' }
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$prometheus = Join-Path $root 'docker/prometheus'
$run = Join-Path $root ('.runstate/cash-close-alerts/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$passed = $false
try {
    foreach ($arguments in @(@('check','rules','/work/rules/cash-close-reporting.yaml'),@('test','rules','/work/tests/cash-close-reporting.test.yaml'))) {
        & $executable run --rm --network none --mount "type=bind,source=$prometheus,target=/work,readonly" --workdir /work --entrypoint /bin/promtool prom/prometheus:v3.5.0 @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Cash-close Prometheus rule verification failed.' }
    }
    $passed = $true
}
finally {
    @{ passed=$passed; image='prom/prometheus:v3.5.0'; completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); receiverDeliveryVerified=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'verification.json') -Encoding utf8
}
Write-Output "Cash-close alert checks passed; evidence: $run"
