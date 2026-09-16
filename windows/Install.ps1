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

$currentFiles = @(
    'Install.ps1',
    'Parrot.exe',
    'README.md',
    'THIRD-PARTY-NOTICES.txt',
    'licenses/LICENSE-onnxruntime.txt',
    'licenses/LICENSE-parakeet-model.txt',
    'licenses/LICENSE-sherpa-onnx.txt',
    'licenses/NOTICE-onnxruntime-third-party.txt',
    'models/parakeet-tdt-0.6b-v2-int8/decoder.int8.onnx',
    'models/parakeet-tdt-0.6b-v2-int8/encoder.int8.onnx',
    'models/parakeet-tdt-0.6b-v2-int8/joiner.int8.onnx',
    'models/parakeet-tdt-0.6b-v2-int8/tokens.txt',
    'onnxruntime.dll',
    'sherpa-onnx-c-api.dll',
    'sherpa-onnx.dll'
)
$legacyFiles = @('Install.ps1', 'Parrot.exe', 'README.md')

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

function Assert-RelativePath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path) -or
        $Path.Contains('\') -or $Path.Contains(':') -or $Path.Contains('//') -or $Path.EndsWith('/') -or
        $Path -match '(^|/)\.\.(/|$)' -or
        $Path -match '(^|/)\.(/|$)') {
        throw "Unsafe package path: $Path"
    }
}

function Get-RelativePath([string] $Root, [string] $Path) {
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escaped its root.' }
    return $fullPath.Substring($fullRoot.Length).Replace('\', '/')
}

function Assert-ExactTree([string] $Directory, [string[]] $Files) {
    $expected = @{}
    foreach ($relative in @($Files + @('package.json'))) {
        $expected[$relative.ToLowerInvariant()] = 'file'
        $parts = $relative.Split('/')
        for ($index = 1; $index -lt $parts.Length; $index++) {
            $directoryPath = ($parts[0..($index - 1)] -join '/')
            $expected[$directoryPath.ToLowerInvariant()] = 'directory'
        }
    }
    foreach ($item in @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)) {
        Assert-NoReparsePoint $item.FullName
        $relative = Get-RelativePath $Directory $item.FullName
        $key = $relative.ToLowerInvariant()
        if (-not $expected.ContainsKey($key)) {
            throw "The installation folder contains an additional item: $relative"
        }
        $actualType = if ($item.PSIsContainer) { 'directory' } else { 'file' }
        if ($actualType -cne $expected[$key]) { throw "Unexpected package item type: $relative" }
    }
    foreach ($key in $expected.Keys) {
        $path = Join-Path $Directory ($key.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $path)) { throw "The package is missing $key." }
    }
}

function Read-Manifest([string] $Directory, [bool] $AllowLegacy) {
    Assert-NoReparsePoint $Directory
    $path = Join-Path $Directory 'package.json'
    Assert-NoReparsePoint $path
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($manifest.product -ne 'Parrot.Windows') { throw 'Unrecognized Parrot package manifest.' }

    $records = @()
    $expectedFiles = @()
    if ($manifest.schema -eq 1 -and $AllowLegacy) {
        $names = @($manifest.files.PSObject.Properties.Name | Sort-Object)
        $expectedLegacyNames = @(($legacyFiles | Sort-Object))
        if (($names -join '|') -cne ($expectedLegacyNames -join '|')) { throw 'Unexpected legacy package files.' }
        foreach ($name in $names) {
            $records += [pscustomobject]@{ path = $name; bytes = -1; sha256 = [string] $manifest.files.$name }
        }
        $expectedFiles = $legacyFiles
    } elseif ($manifest.schema -eq 2) {
        if ([string] $manifest.version -ne '0.2.0') { throw 'Unrecognized Parrot package version.' }
        $seen = @{}
        foreach ($record in @($manifest.files)) {
            $relative = [string] $record.path
            Assert-RelativePath $relative
            $key = $relative.ToLowerInvariant()
            if ($seen.ContainsKey($key)) { throw "Duplicate package path: $relative" }
            $seen[$key] = $true
            $records += $record
        }
        $actualNames = @($records | ForEach-Object { [string] $_.path } | Sort-Object)
        $expectedNames = @($currentFiles | Sort-Object)
        if (($actualNames -join '|') -cne ($expectedNames -join '|')) { throw 'Unexpected package files.' }
        $expectedFiles = $currentFiles
    } else {
        throw 'Unrecognized Parrot package manifest.'
    }

    foreach ($record in $records) {
        $relative = [string] $record.path
        Assert-RelativePath $relative
        $file = Join-Path $Directory ($relative.Replace('/', '\'))
        Assert-NoReparsePoint $file
        $expectedHash = [string] $record.sha256
        if ($expectedHash -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid checksum for $relative." }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Package file is missing: $relative" }
        if ([long] $record.bytes -ge 0 -and (Get-Item -LiteralPath $file).Length -ne [long] $record.bytes) {
            throw "Size mismatch for $relative."
        }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedHash) {
            throw "Checksum mismatch for $relative."
        }
    }
    Assert-ExactTree $Directory $expectedFiles
    return [pscustomobject]@{ manifest = $manifest; files = @($expectedFiles); schema = [int] $manifest.schema }
}

$sourcePackage = Read-Manifest $PSScriptRoot $false
Assert-NoReparsePoint $parent
Assert-NoReparsePoint $destination
if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ieq [IO.Path]::GetFullPath($destination).TrimEnd('\')) {
    throw 'Run the installer from a newly extracted download, not the installed folder.'
}
$hadPrevious = Test-Path -LiteralPath $destination
if ($hadPrevious) { $null = Read-Manifest $destination $true }

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
    foreach ($relative in @($sourcePackage.files + @('package.json'))) {
        $windowsPath = $relative.Replace('/', '\')
        $target = Join-Path $stage $windowsPath
        $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target))
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $windowsPath) -Destination $target
    }
    $null = Read-Manifest $stage $false
    if ($hadPrevious) { Move-Item -LiteralPath $destination -Destination $backup; $backedUp = $true }
    Move-Item -LiteralPath $stage -Destination $destination
    $installed = $true
    $null = Read-Manifest $destination $false
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
