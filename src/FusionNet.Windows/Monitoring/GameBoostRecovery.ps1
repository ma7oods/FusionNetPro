$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\Modules'
$ctx = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('REQUEST_BASE64')) | ConvertFrom-Json
if ($ctx.SessionId -notmatch '^[a-f0-9]{32}$') { throw 'Invalid recovery identity' }
$directory = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) ('FusionNetPro.GameBoost\' + $ctx.SessionId)
function Save-RecoveryStatus([string]$state, [int]$count, [string]$message) {
    $status = @{ State=$state; RuleCount=$count; Message=$message; StartedUtc=$null } | ConvertTo-Json -Compress
    $temporary = Join-Path $directory ([Guid]::NewGuid().ToString('N') + '.tmp')
    $bytes = [Text.Encoding]::UTF8.GetBytes($status)
    $file = [IO.File]::Open($temporary,'CreateNew','Write','None')
    try { $file.Write($bytes,0,$bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
    Move-Item -LiteralPath $temporary -Destination (Join-Path $directory 'status.json') -Force -ErrorAction Stop
}
$lock = $null
try {
    for ($attempt=0; $attempt -lt 60 -and $null -eq $lock; $attempt++) {
        try { $lock = [IO.File]::Open((Join-Path $directory 'session.lock'),'OpenOrCreate','ReadWrite','None') }
        catch [IO.IOException] { Start-Sleep -Seconds 1 }
    }
    if ($null -eq $lock) { throw 'Recovery lock unavailable' }
    $alive = $false
    try {
        $owner = [Diagnostics.Process]::GetProcessById([int]$ctx.OwnerPid)
        try { $alive = $owner.StartTime.ToUniversalTime().Ticks -eq [long]$ctx.OwnerTicks }
        finally { $owner.Dispose() }
    } catch { }
    $helperAlive = $false
    try {
        $helper = [Diagnostics.Process]::GetProcessById([int]$ctx.HelperPid)
        try { $helperAlive = $helper.StartTime.ToUniversalTime().Ticks -eq [long]$ctx.HelperTicks }
        finally { $helper.Dispose() }
    } catch { }
    if ($alive -and $helperAlive -and [DateTimeOffset]::UtcNow -lt [DateTimeOffset]::Parse($ctx.ExpiresUtc) -and !(Test-Path -LiteralPath $ctx.StopPath)) { exit 0 }
    $names = @($ctx.RuleNames)
    $rules = @(Get-NetFirewallRule -PolicyStore PersistentStore -ErrorAction Stop | Where-Object { $names -contains $_.DisplayName })
    if (@($rules | Where-Object { $_.Group -ne $ctx.Group -or $_.Description -ne $ctx.Group }).Count) { throw 'Rule ownership mismatch' }
    foreach ($rule in $rules) { $rule | Remove-NetFirewallRule -ErrorAction Stop }
    if (@(Get-NetFirewallRule -PolicyStore PersistentStore -ErrorAction Stop | Where-Object { $names -contains $_.DisplayName }).Count) { throw 'Persistent rule removal not verified' }
    if (@(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop | Where-Object { $names -contains $_.DisplayName }).Count) { throw 'Rule removal not verified' }
    $task = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object { $_.TaskName -eq $ctx.TaskName })
    if ($task.Count -gt 1 -or ($task.Count -eq 1 -and $task[0].Description -ne $ctx.Group)) { throw 'Recovery task ownership mismatch' }
    Save-RecoveryStatus 'Restored' 0 'Independent recovery verified session rules absent; task cleanup follows durable status and may retry.'
    if ($task.Count -eq 1) { $task[0] | Unregister-ScheduledTask -Confirm:$false -ErrorAction Stop }
} catch {
    if ($null -ne $lock) {
        try { Save-RecoveryStatus 'Pending' -1 'Independent recovery or task cleanup failed; verification is pending and the task will retry.' } catch { }
    }
    exit 1
}
finally { if ($null -ne $lock) { $lock.Dispose() } }
