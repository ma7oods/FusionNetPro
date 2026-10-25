param(
    [string]$Filter = ""
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
    # Build the runner (also builds the test assembly + Core).
    & dotnet build (Join-Path $PSScriptRoot "TestRunner\TestRunner.csproj") --nologo -v q 2>&1 | ForEach-Object { $PSItem }
    if ($LASTEXITCODE -ne 0) { Write-Output "BUILD_EXIT=$LASTEXITCODE"; exit $LASTEXITCODE }
    $runner = Join-Path $PSScriptRoot "TestRunner\bin\Debug\net10.0\TestRunner.dll"
    if ($Filter) { & dotnet $runner --filter=$Filter } else { & dotnet $runner }
    exit $LASTEXITCODE
}
finally {
    $lock.Dispose()
    try { Remove-Item $lockPath -Force -ErrorAction SilentlyContinue } catch { }
}
