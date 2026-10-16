#requires -Version 7.4
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'first-draft-helpers.ps1')
. (Join-Path $PSScriptRoot 'first-draft-release-helpers.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('nexa-release-it-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot|Out-Null
$count=0
function Rejected([scriptblock]$Action){$bad=$false;try{& $Action}catch{$bad=$true};if(-not $bad){throw 'Release guard accepted invalid input.'};$script:count++}
try{
    foreach($file in @('first-draft-release-helpers.ps1','test-first-draft-release.ps1','test-first-draft-restore.ps1','test-first-draft-release-guards.ps1','test-first-draft-checkout.ps1','test-day-seals.ps1','test-settlement-reporting.ps1','test-settlement-replay.ps1','test-cash-close-portal.ps1')){
        $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
        if($errors){throw "Invalid release script: $file"};$count++
    }
    $dumpA=Join-Path $testRoot 'a.sql';$dumpB=Join-Path $testRoot 'b.sql'
    [IO.File]::WriteAllText($dumpA,"\restrict abc123`nSELECT 'fictional';`n\unrestrict abc123`n")
    [IO.File]::WriteAllText($dumpB,"\restrict xyz456`nSELECT 'fictional';`n\unrestrict xyz456`n")
    if((Get-DraftDumpHash $dumpA) -cne (Get-DraftDumpHash $dumpB)){throw 'Random dump guards affected comparison.'};$count++
    [IO.File]::WriteAllText($dumpB,"\restrict xyz456`nSELECT 'changed';`n\unrestrict xyz456`n")
    if((Get-DraftDumpHash $dumpA) -ceq (Get-DraftDumpHash $dumpB)){throw 'Dump normalization hid changed data.'};$count++
    [IO.File]::WriteAllText($dumpA,"ALTER TABLE public.test OWNER TO app;`nGRANT SELECT ON public.test TO reader;`n")
    [IO.File]::WriteAllText($dumpB,"ALTER TABLE public.test OWNER TO app;`nGRANT UPDATE ON public.test TO reader;`n")
    if((Get-DraftPermissionsHash $dumpA) -ceq (Get-DraftPermissionsHash $dumpB)){throw 'Permission comparison hid changed authority.'};$count++
    [IO.File]::WriteAllText($dumpB,"ALTER TABLE public.test OWNER TO other;`nGRANT SELECT ON public.test TO reader;`n")
    if((Get-DraftPermissionsHash $dumpA) -ceq (Get-DraftPermissionsHash $dumpB)){throw 'Permission comparison hid changed ownership.'};$count++
    [IO.File]::WriteAllText($dumpB,'-- no grants');Rejected {Get-DraftPermissionsHash $dumpB}
    $id=[Guid]::NewGuid().ToString('N');$revision='a'*40
    $dir=Join-Path $testRoot ".runstate/day-seals/$id";New-Item -ItemType Directory -Path $dir -Force|Out-Null
    $path=Join-Path $dir 'verification.json'
    $output=[pscustomobject]@{evidenceKind='draft-release-seals';evidencePath=$path}
    $receipt=@{runId=$id;sourceRevision=$revision;passed=$true;cleanupVerified=$true;caseCount=64}
    function Save {$receipt|ConvertTo-Json|Set-Content $path}
    Save;[void](Read-DraftMatrixEvidence $testRoot @($output) 'draft-release-seals' 'day-seals' 64 $revision);$count++
    Rejected {Read-DraftMatrixEvidence $testRoot @() 'draft-release-seals' 'day-seals' 64 $revision}
    Rejected {Read-DraftMatrixEvidence $testRoot @($output,$output) 'draft-release-seals' 'day-seals' 64 $revision}
    foreach($field in @('passed','cleanupVerified')){$receipt[$field]='true';Save;Rejected {Read-DraftMatrixEvidence $testRoot @($output) 'draft-release-seals' 'day-seals' 64 $revision};$receipt[$field]=$true}
    $receipt.caseCount='64';Save;Rejected {Read-DraftMatrixEvidence $testRoot @($output) 'draft-release-seals' 'day-seals' 64 $revision}
    $receipt.caseCount=63;Save;Rejected {Read-DraftMatrixEvidence $testRoot @($output) 'draft-release-seals' 'day-seals' 64 $revision};$receipt.caseCount=64;Save
    Rejected {Read-DraftMatrixEvidence $testRoot @($output) 'draft-release-seals' 'day-seals' 64 ('b'*40)}
    $output.evidencePath=Join-Path $testRoot 'foreign.json';Rejected {Read-DraftMatrixEvidence $testRoot @($output) 'draft-release-seals' 'day-seals' 64 $revision}
    $trx=Join-Path $testRoot 'result.trx'
    function Trx([int]$Total,[int]$Executed,[int]$Passed){"<TestRun><ResultSummary outcome='Completed'><Counters total='$Total' executed='$Executed' passed='$Passed' failed='0'/></ResultSummary></TestRun>"|Set-Content $trx}
    Trx 2 2 2;[void](Read-DraftTestCounts $trx 2);$count++
    Trx 2 1 1;Rejected {Read-DraftTestCounts $trx 2}
    Trx 0 0 0;Rejected {Read-DraftTestCounts $trx 1}
    Trx 1 1 1;Rejected {Read-DraftTestCounts $trx 2}
    $state=@{runId=$id;project="nexa-draft-$id";status='Stopped';processes=@{};urls=@{Order='http://127.0.0.1:22001/';Bff='https://127.0.0.1:22002/'}}
    Assert-DraftRestoreState $state $id;$count++
    $state.status='Ready';Rejected {Assert-DraftRestoreState $state $id};$state.status='Stopped'
    $state.processes.Order=@{id=1};Rejected {Assert-DraftRestoreState $state $id};$state.processes=@{}
    $state.project='other';Rejected {Assert-DraftRestoreState $state $id}
    Write-Output "$count release evidence/parser/restore checks passed."
}finally{
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if((Split-Path $resolved -Leaf) -cnotmatch '^nexa-release-it-[a-f0-9]{32}$' -or (Split-Path $resolved -Parent) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')){throw 'Unexpected release guard cleanup target.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
