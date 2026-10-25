$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path $PSScriptRoot ('recovery-fixture-' + [Guid]::NewGuid().ToString('N'))
if (![IO.Path]::GetFullPath($root).StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped workspace' }
[IO.Directory]::CreateDirectory($root) | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $workspace 'src/FusionNet.Windows/Monitoring/GameBoostRecovery.ps1'))
$shell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$passed = 0
try {
    foreach ($mode in @('success', 'task-delete-fails', 'query-fails', 'rule-delete-fails', 'status-save-fails')) {
        $directory = Join-Path $root $mode
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $context = @{SessionId=[Guid]::NewGuid().ToString('N'); OwnerPid=2147483646; OwnerTicks=1; HelperPid=2147483645; HelperTicks=1;
            ExpiresUtc='1970-01-01T00:00:00Z'; StopPath=(Join-Path $directory 'stop'); RuleNames=@('synthetic-rule'); Group='synthetic-group'; TaskName='synthetic-task'}
        [IO.File]::WriteAllText((Join-Path $directory 'status.json'), '{"State":"Active","RuleCount":1,"Message":"synthetic"}')
        $script = $source.Replace('REQUEST_BASE64', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($context | ConvertTo-Json -Compress))))
        $script = $script.Replace('} catch { exit 1 }', '} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }')
        $assignment = '$directory = ''' + $directory.Replace("'", "''") + "'"
        $script = [regex]::Replace($script, '(?m)^\$directory = .*\r?$', [Text.RegularExpressions.MatchEvaluator]{ param($match) $assignment })
        if ($script.Contains("'CommonApplicationData'")) { throw 'Real recovery path still present in fixture' }
        $stubs = @'
param([string]$Mode)
$global:removed = $false
function Get-NetFirewallRule {
    param($PolicyStore)
    if ($Mode -eq 'query-fails') { throw 'synthetic query failure' }
    if ($Mode -eq 'rule-delete-fails' -and !$global:removed) { [pscustomobject]@{DisplayName='synthetic-rule';Group='synthetic-group';Description='synthetic-group'} }
}
function Remove-NetFirewallRule { param([Parameter(ValueFromPipeline=$true)]$Rule) process { throw 'synthetic rule deletion failure' } }
function Get-ScheduledTask { param($TaskPath) [pscustomobject]@{TaskName='synthetic-task';Description='synthetic-group'} }
function Unregister-ScheduledTask {
    param([Parameter(ValueFromPipeline=$true)]$Task,[switch]$Confirm)
    process {
        $saved = Get-Content -LiteralPath (Join-Path $directory 'status.json') -Raw | ConvertFrom-Json
        if ($saved.State -ne 'Restored') { throw 'Task cleanup preceded durable rule absence' }
        if ($Mode -eq 'task-delete-fails') { throw 'synthetic task deletion failure' }
        [IO.File]::WriteAllText((Join-Path $directory 'task-removed'), 'synthetic')
    }
}
function Move-Item {
    param($LiteralPath,$Destination,[switch]$Force)
    if ($Mode -eq 'status-save-fails') { throw 'synthetic durable status failure' }
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -Force -ErrorAction Stop
}
'@
        $path = Join-Path $directory 'fixture.ps1'
        [IO.File]::WriteAllText($path, $stubs + [Environment]::NewLine + $script)
        & $shell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $path -Mode $mode
        $result = $LASTEXITCODE
        $status = Get-Content -LiteralPath (Join-Path $directory 'status.json') -Raw | ConvertFrom-Json
        if ($mode -eq 'success') {
            if ($result -ne 0 -or $status.State -ne 'Restored' -or !(Test-Path -LiteralPath (Join-Path $directory 'task-removed'))) { throw "$mode failed: exit=$result, state=$($status.State)" }
        } else {
            if ($result -eq 0 -or (Test-Path -LiteralPath (Join-Path $directory 'task-removed'))) { throw "$mode incorrectly succeeded/removed recovery" }
            if ($mode -ne 'status-save-fails' -and $status.State -ne 'Pending') { throw "$mode incorrectly persisted $($status.State)" }
            if ($mode -eq 'status-save-fails' -and $status.State -ne 'Active') { throw 'Failed save changed durable status' }
        }
        $passed++
        Write-Output "PASS $mode"
    }
    Write-Output "Independent recovery: $passed/5 passed; all Firewall/task commands shadowed, workspace-only files."
} finally {
    # The absolute GUID fixture was created and checked above; never delete any real recovery directory.
    Remove-Item -LiteralPath $root -Recurse -Force
}
