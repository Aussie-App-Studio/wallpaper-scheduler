param(
    [switch]$SelfSign,
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"

$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet-sdk;$env:PATH"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$makeappx = Join-Path $sdkBin "makeappx.exe"
$signtool = Join-Path $sdkBin "signtool.exe"

if (!(Test-Path $makeappx)) { throw "makeappx.exe not found at $makeappx" }

# 1. Generate icon assets if missing
& "$PSScriptRoot\generate-assets.ps1" -OutDir "$PSScriptRoot\Assets"

$stage = Join-Path $PSScriptRoot "out\stage"
$outMsix = Join-Path $PSScriptRoot "out\WallpaperScheduler_1.0.0.0_x64.msix"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# 2. Publish App (framework-dependent on Store-provided WindowsAppRuntime 1.8)
Write-Host "Publishing WallpaperScheduler.App ($Configuration)..."
dotnet publish "$root\src\WallpaperScheduler.App\WallpaperScheduler.App.csproj" `
    -c $Configuration -r win-x64 -p:Platform=x64 -p:SelfContained=true `
    -p:WindowsAppSDKSelfContained=false `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }

# 3. Publish Agent (self-contained single-file with partial trim of BCL so it stays small and independent)
Write-Host "Publishing WallpaperScheduler.Agent ($Configuration)..."
$agentStage = Join-Path $PSScriptRoot "out\agent"
if (Test-Path $agentStage) { Remove-Item $agentStage -Recurse -Force }
dotnet publish "$root\src\WallpaperScheduler.Agent\WallpaperScheduler.Agent.csproj" `
    -c $Configuration -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true `
    -p:PublishTrimmed=true -p:TrimMode=partial `
    -o $agentStage --nologo
if ($LASTEXITCODE -ne 0) { throw "Agent publish failed" }
Copy-Item "$agentStage\WallpaperSchedulerAgent.exe" "$stage\WallpaperSchedulerAgent.exe" -Force

# 4. Copy manifest (makeappx requires AppxManifest.xml) + assets
Copy-Item "$PSScriptRoot\Package.appxmanifest" "$stage\AppxManifest.xml" -Force
New-Item -ItemType Directory -Force -Path "$stage\Assets" | Out-Null
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
