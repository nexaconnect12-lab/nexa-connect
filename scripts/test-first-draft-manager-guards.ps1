#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-manager-helpers.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('nexa-manager-it-'+[Guid]::NewGuid().ToString('N'))
$id=[Guid]::NewGuid().ToString('N');$directory=Join-Path $testRoot ".runstate/first-draft/$id"
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$path=Join-Path $directory 'checkout-verification.json'
$output=[pscustomobject]@{evidenceKind='draft-manager-normal';evidencePath=$path}
$receipt=@{runId=$id;passed=$true;cleanupVerified=$true;sourceRevision=('a'*40);checks=@{baseline=$true;interrupted=$true;recovered=$true;'broker-paused'=$true;'broker-resumed'=$true;owningDatabaseEffects=$true;manager=$true}}
$count=0
function Save { $receipt|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $path }
function Rejected([scriptblock]$Action){$rejected=$false;try{& $Action}catch{$rejected=$true};if(-not $rejected){throw 'Manager guard accepted invalid evidence.'};$script:count++}
try{
    foreach($file in @('test-first-draft-manager.ps1','first-draft-manager-helpers.ps1','test-first-draft-manager-guards.ps1','test-first-draft-checkout.ps1','test-financial-completeness-portal.ps1')){
        $tokens=$null;$parseErrors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$parseErrors)
        if($parseErrors){throw 'Manager script syntax is invalid.'};$count++
    }
    Save;[void](Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-normal');$count++
    Rejected {Read-DraftManagerEvidence $testRoot @($output,$output) 'draft-manager-normal'}
    Rejected {Read-DraftManagerEvidence $testRoot @() 'draft-manager-normal'}
    $receipt.passed=$false;Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-normal'};$receipt.passed=$true
    $receipt.cleanupVerified=$false;Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-normal'};$receipt.cleanupVerified=$true
    $receipt.checks.manager='true';Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-normal'};$receipt.checks.manager=$true
    $receipt.runId=[Guid]::NewGuid().ToString('N');Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-normal'};$receipt.runId=$id
    $output.evidencePath=Join-Path $testRoot 'unowned.json';Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-normal'}
    $directory=Join-Path $testRoot ".runstate/cashier-day-cutoff/$id";New-Item -ItemType Directory -Path $directory -Force|Out-Null
    $path=Join-Path $directory 'verification.json';$output=[pscustomobject]@{evidenceKind='draft-manager-historical';evidencePath=$path}
    $receipt=@{runId=$id;passed=$true;cleanupVerified=$true;authorizationPassed=$true;businessTimeZone='Asia/Singapore';expectedBrowserScenarios=18;sourceRevision=('a'*40)}
    Save;[void](Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-historical');$count++
    $receipt.businessTimeZone='Asia/Bangkok';Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-historical'};$receipt.businessTimeZone='Asia/Singapore'
    $receipt.expectedBrowserScenarios=17;Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-historical'};$receipt.expectedBrowserScenarios=18
    $receipt.authorizationPassed=$false;Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-historical'}
    $receipt.authorizationPassed='true';Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-historical'};$receipt.authorizationPassed=$true
    $receipt.expectedBrowserScenarios='18';Save;Rejected {Read-DraftManagerEvidence $testRoot @($output) 'draft-manager-historical'}
    Write-Output "$count manager acceptance safety checks passed."
}finally{
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if((Split-Path $resolved -Leaf) -cnotmatch '^nexa-manager-it-[a-f0-9]{32}$' -or (Split-Path $resolved -Parent) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')){throw 'Unexpected manager guard cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
