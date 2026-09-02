function Assert-ReviewJoinedProject([string] $ProjectName) {
    if ($ProjectName -cnotmatch '^nexa-review-joined-[a-f0-9]{32}$') { throw 'Joined acceptance project must use a generated UUID name.' }
}

function Get-ReviewJoinedComposeArguments([string] $RepositoryRoot,[string] $ProjectName) {
    Assert-ReviewJoinedProject $ProjectName
    $directory=Join-Path $RepositoryRoot 'docker/payment-review-joined'
    return @('compose','--env-file',(Join-Path $directory '.env.example'),'-f',(Join-Path $directory 'compose.yaml'),'-p',$ProjectName)
}

function ConvertFrom-ReviewJoinedPort([string] $PublishedAddress) {
    if($PublishedAddress -cnotmatch '^127\.0\.0\.1:(\d{1,5})$'){throw 'Joined acceptance ports must be published on IPv4 loopback.'}
    $port=[int]$Matches[1]
    if($port -lt 1024 -or $port -gt 65535){throw 'Invalid joined acceptance port.'}
    return $port
}

function New-ReviewJoinedConnection([int]$Port,[string]$RunId,[string]$Suffix,[string]$Username,[string]$Password) {
    if($RunId -cnotmatch '^[a-f0-9]{32}$' -or $Suffix -notin @('platform','restaurant','authorization','order')){throw 'Unsafe joined database identity.'}
    if($Port -lt 1024 -or $Port -gt 65535){throw 'Invalid joined database port.'}
    $value=New-Object System.Data.Common.DbConnectionStringBuilder
    $value['Host']='127.0.0.1';$value['Port']=$Port;$value['Database']="nexa_review_it_${RunId}_${Suffix}";$value['Username']=$Username;$value['Password']=$Password
    return $value.ConnectionString
}

function Get-ReviewJoinedFreePort {
    $listener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
    try{$listener.Start();return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port}finally{$listener.Stop()}
}

function Get-ReviewJoinedFreePorts([int]$Count) {
    if($Count -lt 1 -or $Count -gt 16){throw 'Invalid joined port count.'}
    $listeners=@()
    try{
        1..$Count|ForEach-Object{$listener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0);$listener.Start();$listeners+=$listener}
        return @($listeners|ForEach-Object{([System.Net.IPEndPoint]$_.LocalEndpoint).Port})
    }finally{$listeners|ForEach-Object{$_.Stop()}}
}
