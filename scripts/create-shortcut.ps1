<#
.SYNOPSIS
    Creates WindowsMouseMods.lnk at the repo root, pointing at the latest Release build.

.DESCRIPTION
    .lnk files contain absolute paths so they are machine-specific and gitignored.
    Run this once after cloning (or whenever the build output path changes) to get
    a clickable launcher at the project root.

.PARAMETER Configuration
    Build configuration to target. Defaults to Release.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "src\WindowsMouseMods"
$exePath    = Join-Path $projectDir "bin\$Configuration\net9.0-windows\WindowsMouseMods.exe"
$shortcut   = Join-Path $repoRoot "WindowsMouseMods.lnk"

if (-not (Test-Path $exePath)) {
    Write-Host "Building $Configuration first..." -ForegroundColor Cyan
    dotnet build (Join-Path $projectDir "WindowsMouseMods.csproj") -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($shortcut)
$lnk.TargetPath       = $exePath
$lnk.WorkingDirectory = Split-Path -Parent $exePath
$lnk.Description      = "Windows Mouse Mods - RMB hold/lock utility"
$lnk.IconLocation     = "$exePath,0"
$lnk.Save()

Write-Host "Created shortcut: $shortcut" -ForegroundColor Green
Write-Host "  -> $exePath"
