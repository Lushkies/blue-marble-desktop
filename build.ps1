# Desktop Earth Build Script
# Builds self-contained executables for x64 and ARM64

param(
    [ValidateSet("x64", "arm64", "all")]
    [string]$Target = "all",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectPath = "src\DesktopEarth\DesktopEarth.csproj"
$AssetsDir = "assets"

function Build-Target($rid, $outputDir) {
    Write-Host "`n=== Building $rid ($Configuration) ===" -ForegroundColor Cyan

    $publishDir = "publish\$rid"

    # Clean previous build
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    # Publish self-contained
    dotnet publish $ProjectPath `
        -c $Configuration `
        -r "win-$rid" `
        --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed for $rid" -ForegroundColor Red
        exit 1
    }

    # Copy assets
    Write-Host "Copying assets..." -ForegroundColor Yellow
    $targetAssets = Join-Path $publishDir "assets"
    if (!(Test-Path $targetAssets)) {
        New-Item -ItemType Directory -Path $targetAssets | Out-Null
    }
    Copy-Item -Recurse -Force "$AssetsDir\textures" "$targetAssets\textures"

    # Copy icon to Resources in output
    $targetResources = Join-Path $publishDir "Resources"
    if (!(Test-Path $targetResources)) {
        New-Item -ItemType Directory -Path $targetResources | Out-Null
    }
    Copy-Item "src\DesktopEarth\Resources\desktopearth.ico" "$targetResources\desktopearth.ico"

    # For ARM64, copy Mesa3D if available
    if ($rid -eq "arm64") {
        $mesaSrc = "lib\mesa3d\opengl32.dll"
        if (Test-Path $mesaSrc) {
            Write-Host "Copying Mesa3D fallback DLL..." -ForegroundColor Yellow
            Copy-Item $mesaSrc $publishDir
        } else {
            Write-Host "WARNING: Mesa3D opengl32.dll not found at $mesaSrc" -ForegroundColor Yellow
            Write-Host "  ARM64 build may not work without GPU OpenGL support" -ForegroundColor Yellow
        }
    }

    # Report size
    $exePath = Join-Path $publishDir "DesktopEarth.exe"
    if (Test-Path $exePath) {
        $size = (Get-Item $exePath).Length / 1MB
        Write-Host "Build complete: $exePath ({0:N1} MB)" -f $size -ForegroundColor Green
    }

    # List output
    Write-Host "`nOutput directory contents:" -ForegroundColor Gray
    Get-ChildItem $publishDir -Recurse | Where-Object { !$_.PSIsContainer } |
        ForEach-Object { "  {0} ({1:N1} KB)" -f $_.FullName.Replace("$PWD\", ""), ($_.Length / 1KB) }
}

# Main
Write-Host "Desktop Earth Build" -ForegroundColor White
Write-Host "===================" -ForegroundColor White

switch ($Target) {
    "x64"   { Build-Target "x64" }
    "arm64" { Build-Target "arm64" }
    "all"   { Build-Target "x64"; Build-Target "arm64" }
}

Write-Host "`nDone!" -ForegroundColor Green
