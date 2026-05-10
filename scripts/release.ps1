<#
.SYNOPSIS
    Build, sign, and optionally release a single-file Windows Right-Click Lock executable.

.DESCRIPTION
    Publishes WindowsRightClickLock.csproj as a single-file self-contained x64 .exe,
    signs it with the OK Studio Inc. EV certificate via signtool, verifies the
    signature, copies the artifact to release/, and prints its SHA-256.

    Run from a NON-ELEVATED PowerShell with the SafeNet eToken plugged in.
    The eToken is only visible to the user session; elevated shells get
    "Cannot find certificate."

.PARAMETER Tag
    If supplied, also creates an annotated git tag (vX.Y.Z) and a GitHub
    Release with the signed binary attached. Requires gh CLI authenticated
    and the working tree to be clean.

.PARAMETER CertThumbprint
    SHA-1 thumbprint of the code-signing cert to use. Defaults to the OK
    Studio Inc. EV cert shared across the accessibility-tool ecosystem.

.PARAMETER SelfContained
    If $false, publishes a framework-dependent build (smaller, but testers
    need .NET 9 runtime). Default $true.

.PARAMETER SkipCleanCheck
    Skip the "working tree must be clean" guard. Useful for local smoke
    builds; never use when producing a tagged release.

.PARAMETER NotesFile
    Path to a markdown file whose contents are used as the GitHub Release
    notes body. If omitted, a minimal default body (SHA + SmartScreen
    note) is generated. Only used when -Tag is also supplied.

.EXAMPLE
    pwsh scripts/release.ps1
    # Builds + signs to release/WindowsRightClickLock-0.1.0.exe

.EXAMPLE
    pwsh scripts/release.ps1 -Tag
    # Builds + signs + tags v0.1.0 + creates GitHub Release.
#>
[CmdletBinding()]
param(
    [switch]$Tag,
    [string]$CertThumbprint = 'fc22b5221318f3f3f6b3eb2d969d7f99091557bf',
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [bool]$SelfContained = $true,
    [switch]$SkipCleanCheck,
    [string]$NotesFile
)

$ErrorActionPreference = 'Stop'

function Test-IsElevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-SignTool {
    $pfx86 = ${env:ProgramFiles(x86)}
    if (-not $pfx86) { $pfx86 = 'C:\Program Files (x86)' }
    $kits = Join-Path $pfx86 'Windows Kits\10\bin'

    $candidates = @()
    if (Test-Path $kits) {
        Get-ChildItem $kits -Directory |
            Sort-Object Name -Descending |
            ForEach-Object {
                $p = Join-Path $_.FullName 'x64\signtool.exe'
                if (Test-Path $p) { $candidates += $p }
            }
    }
    $ack = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe'
    if (Test-Path $ack) { $candidates += $ack }
    $onPath = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Path
    if ($onPath) { $candidates += $onPath }

    if (-not $candidates) {
        throw "signtool.exe not found. Install the Windows SDK or add signtool.exe to PATH."
    }
    return $candidates[0]
}

function Invoke-SignTool {
    param(
        [string]$SignTool,
        [string]$FilePath,
        [string]$Thumbprint,
        [string]$Timestamp,
        [int]$MaxRetries = 5
    )
    for ($i = 1; $i -le $MaxRetries; $i++) {
        Write-Host "[Sign] Attempt $i/$MaxRetries`: $(Split-Path $FilePath -Leaf)"
        & $SignTool sign /sha1 $Thumbprint /fd sha256 /tr $Timestamp /td sha256 $FilePath
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[Sign] Success: $(Split-Path $FilePath -Leaf)"
            return
        }
        if ($i -lt $MaxRetries) {
            $delay = 2 * $i
            Write-Host "[Sign] Retry in ${delay}s (file may be locked by antivirus)..."
            Start-Sleep -Seconds $delay
        }
    }
    throw "signtool failed after $MaxRetries attempts."
}

# --- preflight ---
if (Test-IsElevated) {
    throw "Run from a non-elevated PowerShell. SafeNet only exposes the eToken to the user session."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($Tag -and -not $SkipCleanCheck) {
    $dirty = git status --porcelain
    if ($dirty) {
        throw "Working tree not clean. Commit or stash before tagging a release.`n$dirty"
    }
}

$csproj = Join-Path $repoRoot 'src\WindowsRightClickLock\WindowsRightClickLock.csproj'
[xml]$proj = Get-Content $csproj
$version = $proj.Project.PropertyGroup.Version
if (-not $version) { throw "Could not read <Version> from $csproj" }
Write-Host "[Release] Version: $version"

# --- publish ---
$publishDir = Join-Path $repoRoot 'src\WindowsRightClickLock\bin\Release\net9.0-windows\win-x64\publish'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

$selfFlag = if ($SelfContained) { 'true' } else { 'false' }
Write-Host "[Release] dotnet publish (self-contained=$selfFlag)..."
dotnet publish $csproj -c Release -r win-x64 `
    --self-contained $selfFlag `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$publishedExe = Join-Path $publishDir 'WindowsRightClickLock.exe'
if (-not (Test-Path $publishedExe)) { throw "Expected output not found: $publishedExe" }

# --- sign + verify ---
$signtool = Find-SignTool
Write-Host "[Sign] Using signtool: $signtool"
Invoke-SignTool -SignTool $signtool -FilePath $publishedExe `
    -Thumbprint $CertThumbprint -Timestamp $TimestampServer

Write-Host "[Verify] signtool verify /pa /v"
& $signtool verify /pa /v $publishedExe
if ($LASTEXITCODE -ne 0) { throw "Signature verification failed." }

# --- stage release artifact ---
$releaseDir = Join-Path $repoRoot 'release'
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$artifact = Join-Path $releaseDir "WindowsRightClickLock-$version.exe"
Copy-Item -Force $publishedExe $artifact

$hash = (Get-FileHash -Algorithm SHA256 $artifact).Hash.ToLower()
$size = '{0:N1} MB' -f ((Get-Item $artifact).Length / 1MB)

Write-Host ''
Write-Host "[Release] Artifact: $artifact"
Write-Host "[Release] Size:     $size"
Write-Host "[Release] SHA-256:  $hash"

# --- optional tag + gh release ---
if ($Tag) {
    $tagName = "v$version"
    $existing = git tag --list $tagName
    if ($existing) { throw "Tag $tagName already exists." }

    Write-Host "[Release] Tagging $tagName..."
    git tag -a $tagName -m "Release $tagName"
    git push origin $tagName

    if ($NotesFile) {
        if (-not (Test-Path $NotesFile)) { throw "Notes file not found: $NotesFile" }
        $resolvedNotes = (Resolve-Path $NotesFile).Path
        Write-Host "[Release] Using notes from: $resolvedNotes"
        Write-Host "[Release] Creating GitHub Release..."
        gh release create $tagName $artifact --title "$tagName" --notes-file $resolvedNotes
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
    }
    else {
        $notes = @"
Windows Right-Click Lock $tagName

**SHA-256:** ``$hash``

Signed with OK Studio Inc. EV code-signing certificate. SmartScreen reputation
warms up over downloads; if you see a "Windows protected your PC" prompt
during the warm-up window, click *More info* then *Run anyway*.

Run the .exe. Tray icon turns red while a right-click is held by the lock.
"@
        Write-Host "[Release] Creating GitHub Release..."
        $tmpNotes = New-TemporaryFile
        try {
            Set-Content -Path $tmpNotes -Value $notes -Encoding utf8
            gh release create $tagName $artifact --title "$tagName" --notes-file $tmpNotes
            if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
        } finally {
            Remove-Item $tmpNotes -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ''
Write-Host "[Release] Done."
