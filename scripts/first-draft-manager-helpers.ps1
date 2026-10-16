# Evidence validation only. This file has no infrastructure or financial mutations.
Set-StrictMode -Version Latest
function Read-DraftManagerEvidence([string]$Root,$Outputs,[string]$Kind){
    if($Kind -cnotin @('draft-manager-normal','draft-manager-historical')){throw 'Unknown manager evidence kind.'}
    $items=@($Outputs|Where-Object {$_ -is [pscustomobject] -and $_.PSObject.Properties.Name -contains 'evidenceKind' -and $_.evidenceKind -ceq $Kind})
    if($items.Count -ne 1){throw 'Expected one explicitly returned manager evidence receipt.'}
    $path=[IO.Path]::GetFullPath([string]$items[0].evidencePath)
    $folder=if($Kind -eq 'draft-manager-normal'){'first-draft'}else{'cashier-day-cutoff'}
    $name=if($Kind -eq 'draft-manager-normal'){'checkout-verification.json'}else{'verification.json'}
    $runId=Split-Path (Split-Path $path -Parent) -Leaf
    if($runId -cnotmatch '^[a-f0-9]{32}$' -or $path -cne [IO.Path]::GetFullPath((Join-Path $Root ".runstate/$folder/$runId/$name"))){throw 'Manager evidence path is not owned by its stage.'}
    $receipt=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    if($receipt.runId -cne $runId){throw 'Invalid manager evidence identity.'}
    if($receipt.sourceRevision -cnotmatch '^[a-f0-9]{40}$'){throw 'Manager source revision is unavailable.'}
    if($receipt.passed -isnot [bool] -or -not $receipt.passed -or $receipt.cleanupVerified -isnot [bool] -or -not $receipt.cleanupVerified){throw 'Manager stage failed or cleanup was not verified.'}
    if($Kind -eq 'draft-manager-normal'){
        foreach($check in @('baseline','interrupted','recovered','broker-paused','broker-resumed','owningDatabaseEffects','manager')){
            if($receipt.checks.$check -isnot [bool] -or -not $receipt.checks.$check){throw 'Normal-clock manager evidence is incomplete.'}
        }
    }elseif($receipt.authorizationPassed -isnot [bool] -or -not $receipt.authorizationPassed -or $receipt.businessTimeZone -cne 'Asia/Singapore' -or
        ($receipt.expectedBrowserScenarios -isnot [int] -and $receipt.expectedBrowserScenarios -isnot [long]) -or $receipt.expectedBrowserScenarios -ne 18){throw 'Historical manager evidence is incomplete or uses another timezone.'}
    return [pscustomobject]@{runId=$receipt.runId;evidencePath=$path;sourceRevision=$receipt.sourceRevision;cleanupVerified=$true}
}
