#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$path = Join-Path $root 'scripts/verify-pos-cashier-live-acceptance.ps1'
$source = Get-Content -LiteralPath $path -Raw
$parseErrors = $null
$tokens = $null
[void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Cashier live verifier syntax is invalid.' }

$start = $source.IndexOf('$orderSql =')
$end = $source.IndexOf('$localStateDirectory =')
if ($start -lt 0 -or $end -le $start) { throw 'Cashier financial checks could not be located.' }
$checks = [scriptblock]::Create($source.Substring($start, $end - $start))
$OrderId = [Guid]::NewGuid()
$orderText = $OrderId.ToString('D')
$ProjectionWaitSeconds = 1

function Invoke-LocalJsonQuery([string] $Database, [string] $Sql) {
    if ($Sql -notmatch 'BEGIN READ ONLY;' -or $Sql -notmatch 'COMMIT;') {
        throw 'Cashier verifier queries must use read-only transactions.'
    }
    switch ($Database) {
        'NexaConnect_Order' { return $script:orderFixture }
        'NexaConnect_POS' { return $script:posFixture }
        default { throw 'Unexpected cashier acceptance database.' }
    }
}

function New-OrderFixture {
    [pscustomobject]@{
        orderCount = 1; completedOrderCount = 1; cashSettlementCount = 1; settlementAmount = 117.70
        settlementId = [Guid]::NewGuid().ToString('D'); organizationId = [Guid]::NewGuid().ToString('D')
        branchId = [Guid]::NewGuid().ToString('D'); terminalId = [Guid]::NewGuid().ToString('D')
        receiptCount = 1; receiptVersion = 1; receiptNumber = 'R-' + $OrderId.ToString('N').ToUpperInvariant()
        receiptTotal = 117.70; receiptSubtotal = 100; receiptServiceCharge = 10; receiptTax = 7.70
        receiptCurrency = 'THB'; receiptTender = 'cash'; receiptLineCount = 1; storedLineCount = 1
        matchingReceiptLineCount = 1; receiptScopeMatches = $true
    }
}

function New-PosFixture($order) {
    [pscustomobject]@{
        projectionCount = 1; projectionAmount = 117.70; settlementId = $order.settlementId
        organizationId = $order.organizationId; branchId = $order.branchId; terminalId = $order.terminalId
        cashSaleCount = 1; matchedLifecycleCount = 1; closedCashSessionCount = 1; closedShiftCount = 1
    }
}

$cases = @(
    'success', 'unpaid', 'duplicate-settlement', 'missing-receipt', 'bad-receipt-number',
    'receipt-total-mismatch', 'receipt-components-mismatch', 'receipt-line-mismatch',
    'receipt-scope-mismatch', 'missing-projection', 'projection-amount-mismatch', 'open-cash', 'open-shift'
)
foreach ($case in $cases) {
    $script:orderFixture = New-OrderFixture
    $script:posFixture = New-PosFixture $script:orderFixture
    switch ($case) {
        'unpaid' { $script:orderFixture.completedOrderCount = 0 }
        'duplicate-settlement' { $script:orderFixture.cashSettlementCount = 2 }
        'missing-receipt' { $script:orderFixture.receiptCount = 0 }
        'bad-receipt-number' { $script:orderFixture.receiptNumber = 'R-WRONG' }
        'receipt-total-mismatch' { $script:orderFixture.receiptTotal = 118 }
        'receipt-components-mismatch' { $script:orderFixture.receiptTax = 8 }
        'receipt-line-mismatch' { $script:orderFixture.matchingReceiptLineCount = 0 }
        'receipt-scope-mismatch' { $script:orderFixture.receiptScopeMatches = $false }
        'missing-projection' { $script:posFixture.projectionCount = 0; $script:posFixture.cashSaleCount = 0; $script:posFixture.matchedLifecycleCount = 0 }
        'projection-amount-mismatch' { $script:posFixture.projectionAmount = 118 }
        'open-cash' { $script:posFixture.closedCashSessionCount = 0 }
        'open-shift' { $script:posFixture.closedShiftCount = 0 }
    }
    $failure = $null
    try { & $checks } catch { $failure = $_.Exception.Message }
    if ($case -eq 'success' -and $null -ne $failure) { throw "Positive cashier fixture rejected: $failure" }
    if ($case -ne 'success' -and $null -eq $failure) { throw "Unsafe cashier fixture accepted: $case" }
    if ($case -ne 'success' -and $failure -notmatch 'Order acceptance failed:|Receipt acceptance failed:|POS acceptance failed:|Financial acceptance failed:|Projection acceptance failed:') {
        throw "Unexpected cashier fixture failure: $failure"
    }
}

foreach ($case in @('missing-confirmations', 'empty-order')) {
    $failed = $false
    try {
        if ($case -eq 'missing-confirmations') { & $path -OrderId ([Guid]::NewGuid()) }
        else {
            & $path -OrderId ([Guid]::Empty) -ConfirmInteractiveOidc -ConfirmWpfCashCheckout `
                -ConfirmReceiptPreview -ConfirmReceiptReprint -ConfirmReceiptFailureDidNotRetryPayment -ConfirmSignedOut
        }
    }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch 'explicitly confirmed|non-empty UUID') { throw }
    }
    if (-not $failed) { throw "Cashier early guard accepted: $case" }
}

Write-Output 'POS cashier verifier: 13 isolated financial/receipt/projection fixtures and 2 early guards passed. No live database, identity, UI, or printer was accessed.'
