param(
    [switch]$SelfSign
)
$ErrorActionPreference = "Stop"

$sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$env:PATH = "$env:USERPROFILE\.cargo\bin;$sdkBin;$env:PATH"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$makeappx = Join-Path $sdkBin "makeappx.exe"
$signtool = Join-Path $sdkBin "signtool.exe"

if (!(Test-Path $makeappx)) { throw "makeappx.exe not found at $makeappx" }

# 1. Generate icon assets if missing
& "$PSScriptRoot\generate-assets.ps1" -OutDir "$PSScriptRoot\Assets"

$stage = Join-Path $PSScriptRoot "out\stage"
$outMsix = Join-Path $PSScriptRoot "out\WallpaperScheduler_1.0.0.0_x64.msix"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$stage\Assets" | Out-Null

# 2. Build Rust Settings UI
Write-Host "Building WallpaperScheduler UI (release)..."
Push-Location "$root\src\WallpaperScheduler.App.Rs"
try {
    cargo build --release
    if ($LASTEXITCODE -ne 0) { throw "UI build failed" }
} finally {
    Pop-Location
}
Copy-Item "$root\src\WallpaperScheduler.App.Rs\target\release\WallpaperScheduler.exe" "$stage\WallpaperScheduler.exe" -Force

# 3. Build Rust Background Agent
Write-Host "Building WallpaperSchedulerAgent (release)..."
Push-Location "$root\src\WallpaperScheduler.Agent.Rs"
try {
    cargo build --release
    if ($LASTEXITCODE -ne 0) { throw "Agent build failed" }
} finally {
    Pop-Location
}
Copy-Item "$root\src\WallpaperScheduler.Agent.Rs\target\release\WallpaperSchedulerAgent.exe" "$stage\WallpaperSchedulerAgent.exe" -Force

# 4. Copy manifest + assets
Copy-Item "$PSScriptRoot\Package.appxmanifest" "$stage\AppxManifest.xml" -Force
Copy-Item "$PSScriptRoot\Assets\*" "$stage\Assets\" -Recurse -Force

# 5. Pack MSIX
Write-Host "Packing MSIX..."
& $makeappx pack /d $stage /p $outMsix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

if ($SelfSign) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=WallpaperSchedulerDev" } | Select-Object -First 1
    if (!$cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject "CN=WallpaperSchedulerDev" `
            -KeyUsage DigitalSignature -FriendlyName "Wallpaper Scheduler Dev Cert" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
        Export-Certificate -Cert $cert -FilePath "$PSScriptRoot\out\WallpaperSchedulerDev.cer" | Out-Null
    }
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $outMsix
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    Write-Host "Signed local test MSIX: $outMsix"
} else {
    Write-Host "Created Store-ready MSIX: $outMsix"
}
