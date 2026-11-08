# Build kPlugPoseForceExport (Koikatu / Koikatsu Party; one DLL for both) and copy the DLL to ..\Deploy\kPlugPoseForceExport.
# Place this folder directly under the game folder (the project references ..\..\BepInEx and ..\..\Koikatu_Data).
# Requires the .NET SDK.

$ErrorActionPreference = "Stop"

$Project = "$PSScriptRoot\kPlugPoseForceExport\kPlugPoseForceExport.csproj"
$DeployDir = "$PSScriptRoot\..\Deploy\kPlugPoseForceExport"
$Dll = "$PSScriptRoot\kPlugPoseForceExport\bin\Release_KK\net35\kPlugPoseForceExport.dll"

if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Force -Path $DeployDir | Out-Null
}

Write-Host "Building Release_KK..." -ForegroundColor Cyan
dotnet build $Project --configuration Release_KK
if ($LASTEXITCODE -ne 0) {
    Write-Error "Release_KK Build failed!"
    exit 1
}

if (-not (Test-Path $Dll)) {
    Write-Error "kPlugPoseForceExport.dll not found at $Dll!"
    exit 1
}
Copy-Item $Dll -Destination $DeployDir -Force
Write-Host "Deployed: kPlugPoseForceExport.dll -> $DeployDir" -ForegroundColor Green
