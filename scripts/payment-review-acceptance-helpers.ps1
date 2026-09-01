function Assert-ReviewAcceptanceProject([string] $ProjectName) {
    if ($ProjectName -cnotmatch '^nexa-review-it-[a-f0-9]{32}$') {
        throw 'Acceptance project must be a generated nexa-review-it UUID name.'
    }
}

function Get-ReviewAcceptanceComposeArguments([string] $RepositoryRoot, [string] $ProjectName) {
    Assert-ReviewAcceptanceProject $ProjectName
    $directory = Join-Path $RepositoryRoot 'docker/payment-review-acceptance'
    return @('compose', '--env-file', (Join-Path $directory '.env.example'), '-f', (Join-Path $directory 'compose.yaml'), '-p', $ProjectName)
}

function ConvertFrom-ReviewAcceptancePort([string] $PublishedAddress) {
    if ($PublishedAddress -cnotmatch '^127\.0\.0\.1:(\d{1,5})$') { throw 'Acceptance port must be published only on IPv4 loopback.' }
    $port = [int]$Matches[1]
    if ($port -lt 1024 -or $port -gt 65535) { throw 'Invalid acceptance port.' }
    return $port
}

function New-ReviewAcceptanceConnection([int] $Port, [string] $Database, [string] $Password) {
    if ($Database -notin @('postgres','review_order','review_reporting')) { throw 'Database is not an acceptance database.' }
    if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid acceptance port.' }
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = $Port
    $connection['Database'] = $Database
    $connection['Username'] = 'postgres'
    $connection['Password'] = $Password
    return $connection.ConnectionString
}
