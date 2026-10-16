# Candidate identities and strict test receipts; no application or database mutations.
Set-StrictMode -Version Latest
function Get-DraftDumpHash([string]$Path) {
    $text=[IO.File]::ReadAllText($Path)
    # PostgreSQL 17 emits a fresh random \restrict/\unrestrict key on every dump.
    # Do not normalize SQL, row ordering, sequence values, owners or permissions.
    $text=[regex]::Replace($text,'(?m)^\\(?:un)?restrict [A-Za-z0-9]+\r?\n','')
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}
function Get-DraftPermissionsHash([string]$Path) {
    # pg_dump emits each owner/grant/default-privilege statement on one line.
    # Compare these separately from parser-normalized CHECK expression formatting.
    $lines=@([IO.File]::ReadAllLines($Path)|Where-Object {$_ -cmatch '^(ALTER .+ OWNER TO |GRANT |REVOKE |ALTER DEFAULT PRIVILEGES )'})
    if(-not $lines.Count){throw 'Dump contains no ownership/permission evidence.'}
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).ToLowerInvariant()
}
function Get-DraftCandidate([string]$Root) {
    $files=@(& git -C $Root ls-files --cached --others --exclude-standard)
    if($LASTEXITCODE -ne 0){throw 'Candidate inventory failed.'}
    $entries=@(foreach($path in ($files|Sort-Object -Unique)){
        if($path -match '^(src|tests|scripts|docker|\.github)/' -or $path -match '^(global\.json|Directory\..*|NexaConnect\.sln|NuGet\.Config|\.gitattributes)$'){
            if($path -match '\.(md|png|jpg|jpeg|gif|pdf)$'){continue}
            $full=Join-Path $Root $path
            if(Test-Path -LiteralPath $full -PathType Leaf){[pscustomobject]@{path=$path;sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()}}
        }
    })
    if($entries.Count -lt 1){throw 'Candidate is empty.'}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($entries|ForEach-Object {"$($_.path):$($_.sha256)"}) -join "`n")
    [pscustomobject]@{revision=(& git -C $Root rev-parse HEAD).Trim();dirty=[bool](& git -C $Root status --porcelain);inputHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant();files=$entries}
}
function Read-DraftTestCounts([string]$Path,[int]$Minimum) {
    [xml]$result=Get-Content -LiteralPath $Path -Raw
    $counts=$result.TestRun.ResultSummary.Counters
    if($result.TestRun.ResultSummary.outcome -cne 'Completed' -or [int]$counts.total -lt $Minimum -or [int]$counts.executed -ne [int]$counts.total -or [int]$counts.passed -ne [int]$counts.total -or [int]$counts.failed -ne 0){throw 'Required tests did not all execute and pass.'}
    return [int]$counts.total
}
function Assert-DraftRestoreState($State,[string]$RunId) {
    Assert-DraftRunId $RunId
    if($State.runId -cne $RunId -or $State.project -cne "nexa-draft-$RunId" -or $State.status -cne 'Stopped' -or $State.processes.Count -ne 0){throw 'Restore drill requires an exclusively stopped owned draft.'}
    Assert-DraftUrls $State.urls
}
function Read-DraftMatrixEvidence([string]$Root,$Outputs,[string]$Kind,[string]$Folder,[int]$Minimum,[string]$Revision) {
    $items=@($Outputs|Where-Object {$_ -is [pscustomobject] -and $_.PSObject.Properties.Name -contains 'evidenceKind' -and $_.evidenceKind -ceq $Kind})
    if($items.Count -ne 1){throw 'Expected one explicitly returned release matrix receipt.'}
    $path=[IO.Path]::GetFullPath([string]$items[0].evidencePath);$id=Split-Path (Split-Path $path -Parent) -Leaf
    Assert-DraftRunId $id
    if($path -cne [IO.Path]::GetFullPath((Join-Path $Root ".runstate/$Folder/$id/verification.json"))){throw 'Release matrix path ownership mismatch.'}
    $receipt=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    if($receipt.runId -cne $id -or $receipt.sourceRevision -cne $Revision -or $receipt.passed -isnot [bool] -or -not $receipt.passed -or $receipt.cleanupVerified -isnot [bool] -or -not $receipt.cleanupVerified -or $receipt.caseCount -isnot [long] -and $receipt.caseCount -isnot [int] -or $receipt.caseCount -lt $Minimum){throw 'Release matrix failed, skipped cases, changed revision or did not verify cleanup.'}
    return [pscustomobject]@{runId=$id;evidencePath=$path;caseCount=$receipt.caseCount;cleanupVerified=$true}
}
