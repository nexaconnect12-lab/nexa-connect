[CmdletBinding()]
param(
    [switch] $ConfirmDisposableInfrastructure,
    [switch] $ConfirmDestructiveRollback,
    [switch] $NoBuild
)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableInfrastructure -or -not $ConfirmDestructiveRollback){throw 'Confirm disposable infrastructure and destructive POS migration rollback before running.'}
$required=@('NEXACONNECT_POS_INTEGRATION_DB','NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB','NEXACONNECT_RABBITMQ_INTEGRATION_URI')
foreach($name in $required){$value=[Environment]::GetEnvironmentVariable($name);if([string]::IsNullOrWhiteSpace($value)){throw "Missing acceptance setting: $name. Inject it without printing its value."};if($value -match '(?i)(^|[;:/@._-])(prod|production)([;:/@._-]|$)'){throw "Refusing $name because it appears to identify production infrastructure."}}
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$run=Join-Path $root ('.runstate/pos-order-settlement/'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $run -Force|Out-Null
$trx=Join-Path $run 'pos-order-settlement-live-verification.trx';$project=Join-Path $root 'tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj'
$previousEnvironment=$env:NEXACONNECT_ENVIRONMENT;$previousRabbit=$env:NEXACONNECT_RABBITMQ_ACCEPTANCE;$previousMigration=$env:NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE
try{
 $env:NEXACONNECT_ENVIRONMENT='Testing';$env:NEXACONNECT_RABBITMQ_ACCEPTANCE='1';$env:NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE='1'
 if(-not $NoBuild){& dotnet build $project --no-restore --verbosity minimal;if($LASTEXITCODE -ne 0){throw 'POS settlement acceptance build failed.'}}
 $filter='FullyQualifiedName~PosPostgresStoreTests.Order_manual_tenders|FullyQualifiedName~PosPostgresStoreTests.Hosted_consumer|FullyQualifiedName~PosMigrationRunnerAcceptanceTests'
 & dotnet test $project --no-build --no-restore --verbosity minimal --filter $filter --logger "trx;LogFileName=$trx"
 if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $trx)){throw 'POS Order-settlement live verification failed.'}
 [xml]$document=Get-Content -LiteralPath $trx -Raw;$counters=$document.TestRun.ResultSummary.Counters
 if([int]$counters.total -ne 3 -or [int]$counters.passed -ne 3 -or [int]$counters.notExecuted -ne 0){throw "POS settlement evidence incomplete: total=$($counters.total), passed=$($counters.passed), notExecuted=$($counters.notExecuted)."}
 $evidence=[ordered]@{runId=Split-Path $run -Leaf;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');testsPassed=3;migrationLifecycle='0-4-3-4';secretsPrinted=$false;liveUiVerified=$false}
 $evidence|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'evidence.json') -Encoding utf8
 Write-Output "POS settlement live verification passed. Sanitized evidence retained at '$run'."
}finally{$env:NEXACONNECT_ENVIRONMENT=$previousEnvironment;$env:NEXACONNECT_RABBITMQ_ACCEPTANCE=$previousRabbit;$env:NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE=$previousMigration}
