param(
    [Parameter(Mandatory = $true)][string]$Project,
    [switch]$NoRestore
)
$ErrorActionPreference = "Stop"
$lockPath = Join-Path $PSScriptRoot "build.lock"
$lock = $null
for ($i = 0; $i -lt 240 -and -not $lock; $i++) {
    try { $lock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { Start-Sleep -Milliseconds 500 }
}
if (-not $lock) { Write-Error "Could not acquire build lock after 2 minutes."; exit 3 }
try {
    $buildArgs = @("build", $Project, "--nologo", "-v", "q")
    if ($NoRestore) { $buildArgs += "--no-restore" }
    & dotnet @buildArgs 2>&1 | ForEach-Object { $PSItem }
    $code = $LASTEXITCODE
    Write-Output "BUILD_EXIT=$code"
    exit $code
}
finally {
    $lock.Dispose()
    try { Remove-Item $lockPath -Force -ErrorAction SilentlyContinue } catch { }
}
