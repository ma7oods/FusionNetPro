param(
    [switch]$SkipAppPublish,
    [switch]$SkipInstallerPublish
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$lockPath = Join-Path $root "qa\build.lock"
$lock = $null
for ($i = 0; $i -lt 240 -and -not $lock; $i++) {
    try { $lock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { Start-Sleep -Milliseconds 500 }
}
if (-not $lock) { throw "Could not acquire build lock." }

try {
    $payloadAppDir = Join-Path $root "installer\payload\app"
    $stageDir = Join-Path $root "installer\payload\stage"
    $zipPath = Join-Path $root "installer\FusionNet.Setup\payload\payload.zip"
    $distDir = Join-Path $root "dist"
    $version = "3.3.3"

    if (-not $SkipAppPublish) {
        Write-Host "== publishing self-contained app =="
        if (Test-Path $payloadAppDir) { Remove-Item $payloadAppDir -Recurse -Force }
        # WPF's native libraries cannot be loaded from a bundle, so the app keeps the
        # self-extract path and stays a single standalone executable.
        & dotnet publish "src\FusionNet.App\FusionNet.App.csproj" -c Release -r win-x64 --self-contained true --nologo -v q `
            -p:PublishSingleFile=true `
            -p:EnableCompressionInSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:DebugType=none -p:DebugSymbols=false `
            -o $payloadAppDir
        if ($LASTEXITCODE -ne 0) { throw "App publish failed (exit $LASTEXITCODE)." }
    }

    $appExe = Join-Path $payloadAppDir "FusionNetPro.exe"
    if (-not (Test-Path $appExe)) { throw "Published app not found: $appExe" }

    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Force $stageDir | Out-Null
    Copy-Item $appExe (Join-Path $stageDir "FusionNetPro.exe")
    Copy-Item (Join-Path $root "installer\assets\README-installed.txt") (Join-Path $stageDir "README.txt")
    Copy-Item (Join-Path $root "src\FusionNet.App\Assets\THIRD-PARTY-NOTICES.txt") $stageDir
    $appHash = (Get-FileHash (Join-Path $stageDir "FusionNetPro.exe") -Algorithm SHA256).Hash
    Set-Content -Path (Join-Path $stageDir "payload.sha256") -Value $appHash -Encoding ASCII

    New-Item -ItemType Directory -Force (Split-Path $zipPath) | Out-Null
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host ("payload.zip: {0:N1} MB" -f ((Get-Item $zipPath).Length / 1MB))

    if (-not $SkipInstallerPublish) {
        Write-Host "== publishing setup exe =="
        $outDir = Join-Path $root "installer\publish"
        if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
        & dotnet publish "installer\FusionNet.Setup\FusionNet.Setup.csproj" -c Release -r win-x64 --self-contained true --nologo -v q `
            -p:PublishSingleFile=true `
            -p:EnableCompressionInSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=false `
            -p:DebugType=none -p:DebugSymbols=false `
            -o $outDir
        if ($LASTEXITCODE -ne 0) { throw "Setup publish failed (exit $LASTEXITCODE)." }

        New-Item -ItemType Directory -Force $distDir | Out-Null
        $setup = Join-Path $outDir "FusionNetPro-Setup.exe"
        if (-not (Test-Path $setup)) { throw "Setup exe not found: $setup" }
        Copy-Item $setup (Join-Path $distDir "FusionNetPro-Setup-$version.exe") -Force
    }

    # Development/QA build: self-contained but not single-file, so it starts without the
    # %TEMP% extraction step and can be driven by the page-navigation screenshot run.
    $qaAppDir = Join-Path $distDir "app"
    if (Test-Path $qaAppDir) { Remove-Item $qaAppDir -Recurse -Force }
    & dotnet publish "src\FusionNet.App\FusionNet.App.csproj" -c Release -r win-x64 --self-contained true --nologo -v q `
        -p:PublishSingleFile=false `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $qaAppDir
    if ($LASTEXITCODE -ne 0) { throw "App (development) publish failed (exit $LASTEXITCODE)." }

    # Portable artifacts (same self-contained exe)
    Copy-Item $appExe (Join-Path $distDir "FusionNetPro-$version.exe") -Force
    New-Item -ItemType Directory -Force (Join-Path $distDir "single") | Out-Null
    Copy-Item $appExe (Join-Path $distDir "single\FusionNetPro.exe") -Force

    $portableStage = Join-Path $root "installer\payload\portable"
    if (Test-Path $portableStage) { Remove-Item $portableStage -Recurse -Force }
    New-Item -ItemType Directory -Force $portableStage | Out-Null
    Copy-Item (Join-Path $stageDir "FusionNetPro.exe") $portableStage
    Copy-Item (Join-Path $stageDir "README.txt") $portableStage
    Copy-Item (Join-Path $stageDir "THIRD-PARTY-NOTICES.txt") $portableStage
    $portableZip = Join-Path $distDir "FusionNetPro-$version-portable.zip"
    if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($portableStage, $portableZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    # Hashes
    $sums = @()
    foreach ($name in @("FusionNetPro-Setup-$version.exe", "FusionNetPro-$version.exe", "FusionNetPro-$version-portable.zip")) {
        $path = Join-Path $distDir $name
        if (Test-Path $path) {
            $hash = (Get-FileHash $path -Algorithm SHA256).Hash
            $sums += ("{0}  {1}" -f $hash, $name)
        }
    }
    Set-Content -Path (Join-Path $distDir "SHA256SUMS.txt") -Value $sums -Encoding ASCII

    Write-Host "== artifacts =="
    Get-ChildItem $distDir -File | Where-Object { $_.Name -match '^FusionNetPro-|SHA256SUMS' } |
        Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } |
        Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
finally {
    $lock.Dispose()
    try { Remove-Item $lockPath -Force -ErrorAction SilentlyContinue } catch { }
}

