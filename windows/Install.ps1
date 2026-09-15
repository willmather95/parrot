<#
.SYNOPSIS
Install or update an extracted Parrot Windows package for the current user.
.DESCRIPTION
No elevation, service, driver, policy changes, automatic start, or downloads.
Portable use of Parrot.exe requires no installer. Close Parrot before updates.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This installer requires Windows.' }
$localData = [Environment]::GetFolderPath('LocalApplicationData')
if ([string]::IsNullOrWhiteSpace($localData)) { throw 'The current user application-data directory is unavailable.' }
$parent = Join-Path $localData 'Programs'
$destination = Join-Path $parent 'Parrot'

function Assert-NoReparsePoint([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a redirected installation path: $current"
            }
        }
        $next = [IO.Path]::GetDirectoryName($current)
        if ($next -eq $current) { break }
        $current = $next
    }
}

function Read-Manifest([string] $Directory) {
    Assert-NoReparsePoint $Directory
    $path = Join-Path $Directory 'package.json'
    Assert-NoReparsePoint $path
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($manifest.product -ne 'Parrot.Windows' -or $manifest.schema -ne 1) { throw 'Unrecognized Parrot package manifest.' }
    $names = @($manifest.files.PSObject.Properties.Name | Sort-Object)
    if (($names -join '|') -ne 'Install.ps1|Parrot.exe|README.md') { throw 'Unexpected package files.' }
    foreach ($name in $names) {
        $file = Join-Path $Directory $name
        Assert-NoReparsePoint $file
        $expected = [string] $manifest.files.$name
        if ($expected -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid checksum for $name." }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) { throw "Checksum mismatch for $name." }
    }
    return $manifest
}

$null = Read-Manifest $PSScriptRoot
Assert-NoReparsePoint $parent
Assert-NoReparsePoint $destination
if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ieq [IO.Path]::GetFullPath($destination).TrimEnd('\')) {
    throw 'Run the installer from a newly extracted download, not the installed folder.'
}
$hadPrevious = Test-Path -LiteralPath $destination
if ($hadPrevious) {
    $null = Read-Manifest $destination
    foreach ($item in @(Get-ChildItem -LiteralPath $destination -Force)) {
        if ($item.Name -notin @('Parrot.exe', 'README.md', 'Install.ps1', 'package.json') -or $item.PSIsContainer) {
            throw 'The installed folder contains additional files. Preserve them and use the portable app or resolve the folder manually.'
        }
    }
}
# Never terminate a dictation or replace its active binary.
foreach ($process in @(Get-Process -Name Parrot -ErrorAction SilentlyContinue)) {
    try { $runningPath = $process.Path } catch { throw 'Cannot verify whether Parrot is running. Close it before installing.' }
    if ([string]::IsNullOrWhiteSpace($runningPath) -or $runningPath -ieq (Join-Path $destination 'Parrot.exe')) {
        throw 'Close Parrot before installing this update. No files were replaced.'
    }
}
$null = New-Item -ItemType Directory -Force -Path $parent
$token = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $parent ('.Parrot-stage-' + $token)
$backup = Join-Path $parent ('.Parrot-backup-' + $token)
$installed = $false
$backedUp = $false
$null = New-Item -ItemType Directory -Path $stage
try {
    foreach ($name in @('Parrot.exe', 'README.md', 'Install.ps1', 'package.json')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage
    }
    $null = Read-Manifest $stage
    if ($hadPrevious) { Move-Item -LiteralPath $destination -Destination $backup; $backedUp = $true }
    Move-Item -LiteralPath $stage -Destination $destination
    $installed = $true
    $null = Read-Manifest $destination
} catch {
    $failure = $_
    if ($installed) { Remove-Item -LiteralPath $destination -Recurse -Force }
    if ($backedUp) { Move-Item -LiteralPath $backup -Destination $destination; $backedUp = $false }
    throw $failure
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
if ($backedUp) { Remove-Item -LiteralPath $backup -Recurse -Force }
Write-Host "Installed files at $destination"
Write-Host 'Open Parrot.exe there. This verifies the package, not microphone access or recognition quality.'
Write-Host 'To update, close Parrot and run Install.ps1 from a new extracted package.'
