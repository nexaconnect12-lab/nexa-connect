# Static operator guidance over the existing validated record. No event or readiness promotion.
function Get-PosAcceptanceWorklist($Record,[bool]$BindingValid,[bool]$QrCandidateFresh) {
    $report=Get-PosAcceptanceReport $Record $BindingValid $QrCandidateFresh
    $guidance=@{
        'cashier-signin'=@('Access','Original cashier and configured test branch/terminal.','Sign in normally; check branch, terminal and retained custody.','Only the intended cashier accesses the original workspace.')
        'idle-lock-resume'=@('Access','Signed-in original cashier; configured idle interval.','Wait for idle lock; tap or press a key; resume as the original cashier.','Private work stays hidden until authentication; custody is retained.')
        'session-expiry-fallback'=@('Access','Separate test identity context with genuine expiry or denied refresh.','Observe expiry and interactive fallback; try another cashier, then the original cashier.','Private actions lock; only the original cashier restores retained work. Do not edit tokens or shorten retained policy.')
        'cash-original-settlement'=@('Payments','Dedicated test cash session and synthetic sale.','Review the server total; settle once in cash; refresh and review the original order and receipt.','One paid order and exact cash movement; no second collection.')
        'promptpay-original-settlement'=@('Payments','Approved branch PNG and authorized test proof; avoid real transfers.','Check QR payee and server amount; verify proof and confirm once; review the original order.','One settlement with the original method, amount and receipt; no duplicate.')
        'omise-sandbox-original'=@('Payments','Existing sandbox-enabled profile and fresh test token; no real card or production key.','Select Omise before placement; pay once through the established sandbox flow; review original outcome.','One original provider/order outcome and receipt; no duplicate capture.')
        'qr-cash-collection'=@('QR ordering','Trusted iPhone Safari and Android Chrome; guest table QR; staff and Kitchen access.','Submit a synthetic guest request; verify separate guest histories; staff accepts; collect the original order in cash.','Kitchen receives once after acceptance; POS collects once at the original server total.')
        'qr-promptpay-collection'=@('QR ordering','Different synthetic guest request; approved PNG and authorized test proof.','Submit and accept the request; check Kitchen; collect the selected original order with manual PromptPay.','Separate guest histories and one original-order settlement; no duplicate Kitchen delivery.')
        'restart-pending-payment'=@('Recovery','Separate dedicated test run and saved original pending request.','Close and reopen POS; authenticate; explicitly recover the saved original request without deleting recovery.','Same method, amount and idempotency identity; one final financial outcome.')
        'network-loss-original-retry'=@('Recovery','Isolated test stack and controlled uncertainty point; no shared service interruption.','Interrupt the owning dependency; restore it; review or retry the saved original request.','Exactly one settlement or capture; uncertainty never creates a fresh payment.')
        'receipt-pdf-reprint'=@('Receipts','Paid synthetic original order and test PDF printer.','Save a PDF, then use authorized reprint; inspect totals, tender, pagination and readability.','Readable exact receipt; reprint does not change payment.')
        'printer-failure-recovery'=@('Receipts','Test printer/profile; paid synthetic original order.','Cancel or fail test printing; review Paid state; reprint explicitly afterward.','Payment and recovery survive print failure. Physical PAISHUO output needs its own observation.')
        'barcode-input'=@('Equipment and privacy','Purchased scanner and registered, unknown and disabled product codes.','Scan deliberately and repeat; scan while proof or authentication input is active.','Only intended scan-to-cart changes the correct cart; tender and text fields remain separate. Block if hardware is unavailable.')
        'dual-display-isolation'=@('Equipment and privacy','Actual two-screen POS; record measured resolution/scaling privately.','Check staff/customer assignment; disconnect and reconnect; check one-screen fallback.','Customer screen exposes no authentication, proof, history or diagnostics.')
        'locked-gallery'=@('Equipment and privacy','Approved staff/customer playlists refreshed in POS.','Lock and inspect both screens; interact to reveal centered authentication; observe media expiry/failure.','Appropriate full-screen media without private work; safe fallback and original-cashier resume.')
        'drawer-history-privacy'=@('Equipment and privacy','History-only profile; physical drawer opening remains disabled.','Review own terminal/actor history; check lock, sign-out, cashier change, denial and outage clearing.','No cross-scope or stale history. Empty history is valid; never manufacture drawer attempts.')
        'permission-scope-denial'=@('Equipment and privacy','Restricted and wrong-branch identities in a separate test context.','Attempt privileged reads/actions; change identity while a response is delayed.','Authorization denies access; delayed results cannot restore another cashier private view.')
    }
    $items=@(foreach($case in $report.cases){
        $detail=$guidance[$case.id]
        if(-not $detail){throw 'Missing POS operator guidance.'}
        $open=@($report.defects|Where-Object {$_.caseId -ceq $case.id -and $_.status -ceq 'open'})
        $action=if($open.Count){'investigate-defect'}elseif($case.status -ceq 'failed'){'investigate-failure'}elseif($case.status -ceq 'blocked'){'review-prerequisite'}elseif($case.status -ceq 'pending'){'observe'}else{'complete'}
        @{id=$case.id;status=$case.status;group=$detail[0];prerequisite=$detail[1];procedure=$detail[2];expected=$detail[3];nextAction=$action;openDefectCount=$open.Count}
    })
    $next=$null
    if($BindingValid){
        foreach($action in @('investigate-defect','investigate-failure','observe','review-prerequisite')){
            $matches=@($items|Where-Object nextAction -CEQ $action)
            if($matches.Count){$next=@{caseId=$matches[0].id;action=$action};break}
        }
    }else{$next=@{caseId=$null;action='repair-binding'}}
    return @{version=1;kind='operator-worklist';acceptanceId=$report.acceptanceId;target=$report.target;
        bindingValid=$BindingValid;qrCandidateFresh=$QrCandidateFresh;next=$next;cases=$items;
        operatorCasesComplete=$report.operatorCasesComplete;observationsRecorded=$false;
        currentSourceVerified=$false;releaseReady=$false;productionVerified=$false}
}
