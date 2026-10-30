# Local history-only pilot profile. This never enables drawer commands or a physical driver.
function Test-DraftDrawerHistoryEnabled($State){
    if($State.Contains('drawerHistoryEnabled') -and $State.drawerHistoryEnabled -isnot [bool]){throw 'Drawer history selection must be Boolean.'}
    return $State.Contains('drawerHistoryEnabled') -and $State.drawerHistoryEnabled
}
function Enable-DraftDrawerHistory($State,[string]$Action,[bool]$NoBuild){
    if($Action -cne 'Start' -or $NoBuild -or $State.processes.Count){throw 'Drawer history activation requires a stopped, build-enabled Start.'}
    $State.drawerHistoryEnabled=$true
}
function Get-DraftDrawerServiceSettings([string]$Name,$State){
    $enabled=Test-DraftDrawerHistoryEnabled $State
    if($Name -cne 'POS'){return @{}}
    return @{DrawerIntents__Enabled='false';DrawerIntents__HistoryEnabled=$enabled.ToString().ToLowerInvariant()}
}
