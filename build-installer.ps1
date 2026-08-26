#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Monitor/TV Switcher and builds a Windows Setup.exe.

.EXAMPLE
  .\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$Csproj = Join-Path $RepoRoot "GameShortcut.csproj"
$Iss = Join-Path $RepoRoot "installer\MonitorTVSwitcher.iss"
$PublishDir = Join-Path $RepoRoot "artifacts\publish"
$OutputDir = Join-Path $RepoRoot "dist"
$ToolsInno = Join-Path $RepoRoot "tools\innosetup"
$SetupIcon = Join-Path $RepoRoot "Assets\tray.ico"
$InnoSetupVersion = "6.7.3"
$InnoSetupUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe"

function Get-AppVersion {
    $raw = Get-Content -Raw -Path $Csproj
    $match = [regex]::Match($raw, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "Could not read <Version> from GameShortcut.csproj"
    }
    return $match.Groups[1].Value.Trim()
}

function Find-ISCC {
    $candidates = @(
        (Join-Path $ToolsInno "ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            return $path
        }
    }

    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    return $null
}

function Install-InnoSetupCompiler {
    Write-Host "Inno Setup compiler not found. Downloading $InnoSetupVersion..."
    $tempExe = Join-Path $env:TEMP "innosetup-$InnoSetupVersion.exe"
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $InnoSetupUrl -OutFile $tempExe -UseBasicParsing

    New-Item -ItemType Directory -Force -Path $ToolsInno | Out-Null
    Write-Host "Installing Inno Setup compiler to $ToolsInno"
    $process = Start-Process -FilePath $tempExe -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/SP-",
        "/CURRENTUSER",
        "/DIR=$ToolsInno"
    ) -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup installer exited with code $($process.ExitCode)"
    }

    $iscc = Join-Path $ToolsInno "ISCC.exe"
    if (-not (Test-Path -LiteralPath $iscc)) {
        throw "ISCC.exe was not found after installing Inno Setup. Install it from https://jrsoftware.org/isinfo.php or run: winget install JRSoftware.InnoSetup"
    }
    return $iscc
}

if (-not (Test-Path -LiteralPath $Csproj)) {
    throw "Project not found: $Csproj"
}
if (-not (Test-Path -LiteralPath $Iss)) {
    throw "Installer script not found: $Iss"
}

$version = Get-AppVersion
Write-Host "Building Monitor/TV Switcher $version installer"

if (-not $SkipPublish) {
    Write-Host "Publishing self-contained win-x64 build..."
    Get-Process GameShortcut -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet SDK not found on PATH."
    }

    & dotnet publish $Csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -o $PublishDir `
        -p:DebugType=none `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

$exe = Join-Path $PublishDir "GameShortcut.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Published app not found at $exe. Run without -SkipPublish."
}

$iscc = Find-ISCC
if (-not $iscc) {
    $iscc = Install-InnoSetupCompiler
}
Write-Host "Using ISCC: $iscc"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

& $iscc `
    "/DMyAppVersion=$version" `
    "/DPublishDir=$PublishDir" `
    "/DOutputDir=$OutputDir" `
    "/DSetupIcon=$SetupIcon" `
    $Iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setup = Join-Path $OutputDir "MonitorTVSwitcher-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Expected installer was not created: $setup"
}

Write-Host ""
Write-Host "Installer created:"
Write-Host "  $setup"
Write-Host ("  Size: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
