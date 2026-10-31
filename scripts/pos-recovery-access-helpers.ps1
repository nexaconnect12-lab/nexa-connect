# UX20 aggregates existing isolated producers; it cannot grant operator acceptance.
function Assert-PosRecoveryProducer([string]$Producer,$Receipt) {
    if($Producer -ceq 'regression'){
        if($Receipt.kind -cne 'automated-pos-regression' -or $Receipt.passed -cne $true -or
            $Receipt.operatorAcceptance -cne $false -or $Receipt.retainedPilotMutated -cne $false -or
            $Receipt.providerRequests -cne $false -or $Receipt.releaseReady -cne $false -or $Receipt.productionVerified -cne $false){throw 'Invalid isolated POS regression receipt.'}
        foreach($key in @('guards','unit','http','windows-input','windows-print')){
            if($Receipt.checks[$key].passed -cne $true){throw 'Incomplete POS regression checks.'}
        }
        if($Receipt.checks.unit.count -lt 1332 -or $Receipt.checks.http.count -lt 49){throw 'Incomplete POS regression counts.'}
    }elseif($Producer -ceq 'cashier'){
        if($Receipt.automatedBackendPassed -cne $true -or $Receipt.verifierContractPassed -cne $true -or
            $Receipt.cleanupVerified -cne $true -or $Receipt.productionVerified -cne $false -or
            $Receipt.protectedPosCases -lt 21 -or $Receipt.orderReceiptAuthorizationAndMigrationCases -lt 4){throw 'Incomplete disposable cashier recovery or cleanup.'}
    }elseif($Producer -ceq 'workflow'){
        if($Receipt.matrixPassed -cne $true -or $Receipt.cleanupPassed -cne $true -or $Receipt.orderProcessInterruptions -ne 2){throw 'Incomplete disposable workflow recovery or cleanup.'}
    }else{throw 'Unknown recovery producer.'}
}
