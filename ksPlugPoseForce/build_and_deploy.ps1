# Build ksPlugPoseForce (Koikatsu Sunshine) and copy the DLL to ..\Deploy\ksPlugPoseForce.
# Place this folder directly under the game folder (the project references ..\..\BepInEx and ..\..\KoikatsuSunshine_Data).
# Requires the .NET SDK.

$ErrorActionPreference = "Stop"

$Project = "$PSScriptRoot\ksPlugPoseForce\ksPlugPoseForce.csproj"
$DeployDir = "$PSScriptRoot\..\Deploy\ksPlugPoseForce"
$Dll = "$PSScriptRoot\ksPlugPoseForce\bin\Release_KKS\net46\ksPlugPoseForce.dll"

if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Force -Path $DeployDir | Out-Null
}

Write-Host "Building Release_KKS..." -ForegroundColor Cyan
dotnet build $Project --configuration Release_KKS
if ($LASTEXITCODE -ne 0) {
    Write-Error "Release_KKS Build failed!"
    exit 1
}

if (-not (Test-Path $Dll)) {
    Write-Error "ksPlugPoseForce.dll not found at $Dll!"
    exit 1
}
Copy-Item $Dll -Destination $DeployDir -Force
Write-Host "Deployed: ksPlugPoseForce.dll -> $DeployDir" -ForegroundColor Green
