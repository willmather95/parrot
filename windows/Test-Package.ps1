# Integration check exclusively for a disposable GitHub Actions Windows runner.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:OS -ne 'Windows_NT') {
    throw 'Run this installation test only on a disposable GitHub Actions Windows runner.'
}

$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Parrot'
if (Test-Path -LiteralPath $destination) { throw 'Test refuses to touch an existing Parrot installation.' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('parrot-package-test-' + [Guid]::NewGuid().ToString('N'))
$fixtures = Join-Path ([IO.Path]::GetTempPath()) ('parrot-package-fixtures-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
$null = New-Item -ItemType Directory -Path $fixtures
$created = $false

function Get-TreeHashes([string] $Root) {
    $result = @{}
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        $result[$relative] = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    }
    return $result
}

try {
    $archive = Join-Path $PSScriptRoot 'build\parrot-windows-x64-preview.zip'
    $expected = (Get-Content -LiteralPath "$archive.sha256" -Raw).Split(' ')[0]
    if ($expected -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid archive checksum file.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant() -cne $expected) { throw 'Archive hash mismatch.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $scratch

    # Simulate an installed schema-1 preview and prove the schema-2 installer
    # upgrades it transactionally without a manual delete.
    $null = New-Item -ItemType Directory -Force -Path $destination
    foreach ($name in @('Install.ps1', 'Parrot.exe', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $scratch $name) -Destination (Join-Path $destination $name)
    }
    $legacyHashes = @{}
    foreach ($name in @('Install.ps1', 'Parrot.exe', 'README.md')) {
        $legacyHashes[$name] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination $name)).Hash.ToLowerInvariant()
    }
    @{ product = 'Parrot.Windows'; schema = 1; files = $legacyHashes } |
        ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $destination 'package.json')
    $created = $true

    & (Join-Path $scratch 'Install.ps1')
    $installedManifest = Get-Content -LiteralPath (Join-Path $destination 'package.json') -Raw | ConvertFrom-Json
    if ($installedManifest.schema -ne 2 -or ([string] $installedManifest.version) -cne '0.2.0') {
        throw 'Schema-1 installation did not upgrade to schema 2 version 0.2.0.'
    }
    $before = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination 'Parrot.exe')).Hash
    $process = Start-Process -FilePath (Join-Path $destination 'Parrot.exe') -ArgumentList '--self-test' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Installed binary self-test failed.' }

    # An unexpected user file must block an update before the folder is moved.
    $personalFile = Join-Path $destination 'personal-fixture.txt'
    Set-Content -LiteralPath $personalFile -Encoding UTF8 -Value 'preserve me'
    $rejected = $false
    try { & (Join-Path $scratch 'Install.ps1') } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $personalFile -PathType Leaf)) {
        throw 'An installation containing an extra file was not preserved and rejected.'
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination 'Parrot.exe')).Hash -cne $before) {
        throw 'Rejected extra-file update changed the installed binary.'
    }
    Remove-Item -LiteralPath $personalFile -Force

    # A valid replacement remains installable.
    & (Join-Path $scratch 'Install.ps1')
    $previousFiles = Get-TreeHashes $destination

    # Inject a fault into a disposable copy after promotion, forcing the actual
    # installer's catch/rollback path. No fault hooks ship in the real installer.
    $installer = Join-Path $scratch 'Install.ps1'
    $manifestPath = Join-Path $scratch 'package.json'
    Copy-Item -LiteralPath $installer -Destination (Join-Path $fixtures 'Install.ps1')
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $fixtures 'package.json')
    $originalInstaller = Get-Content -LiteralPath $installer -Raw
    $needle = '    $installed = $true'
    if (-not $originalInstaller.Contains($needle)) { throw 'Rollback fault-injection anchor changed.' }
    $injected = $originalInstaller.Replace($needle, $needle + "`r`n    throw 'Injected post-promotion failure'")
    Set-Content -LiteralPath $installer -Encoding UTF8 -Value $injected
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $installerRecords = @($manifest.files | Where-Object { $_.path -ceq 'Install.ps1' })
    if ($installerRecords.Count -ne 1) { throw 'Installer manifest record is missing or duplicated.' }
    $installerRecords[0].bytes = (Get-Item -LiteralPath $installer).Length
    $installerRecords[0].sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $rolledBack = $false
    try { & $installer } catch {
        if ($_.Exception.Message -notlike '*Injected post-promotion failure*') { throw }
        $rolledBack = $true
    }
    if (-not $rolledBack) { throw 'Injected update failure did not fail.' }
    $afterRollback = Get-TreeHashes $destination
    if ($afterRollback.Count -ne $previousFiles.Count) { throw 'Rollback changed the installed file set.' }
    foreach ($name in $previousFiles.Keys) {
        if (-not $afterRollback.ContainsKey($name) -or $afterRollback[$name] -cne $previousFiles[$name]) {
            throw "Rollback did not restore $name."
        }
    }

    # Restore the extracted source package without rewriting the large model.
    Copy-Item -LiteralPath (Join-Path $fixtures 'Install.ps1') -Destination $installer -Force
    Copy-Item -LiteralPath (Join-Path $fixtures 'package.json') -Destination $manifestPath -Force
    Add-Content -LiteralPath $installer -Value '# corrupted installer fixture'
    $rejected = $false
    try { & $installer } catch { $rejected = $true }
    if (-not $rejected) { throw 'Corrupt installer was accepted.' }

    Copy-Item -LiteralPath (Join-Path $fixtures 'Install.ps1') -Destination $installer -Force
    Copy-Item -LiteralPath (Join-Path $fixtures 'package.json') -Destination $manifestPath -Force
    # A corrupted nested model asset must fail before changing the installation.
    $tokens = Join-Path $scratch 'models\parakeet-tdt-0.6b-v2-int8\tokens.txt'
    Add-Content -LiteralPath $tokens -Value 'corrupted fixture'
    $rejected = $false
    try { & $installer } catch { $rejected = $true }
    if (-not $rejected) { throw 'Corrupt nested package file was accepted.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination 'Parrot.exe')).Hash -cne $before) {
        throw 'Corrupt update changed the installed binary.'
    }
    Write-Host 'PASS: schema-1 upgrade, recursive checksums, extra-file preservation, installation, installed self-test, update, forced rollback, and corrupt-update preservation'
} finally {
    if ($created -and (Test-Path -LiteralPath $destination)) { Remove-Item -LiteralPath $destination -Recurse -Force }
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    if (Test-Path -LiteralPath $fixtures) { Remove-Item -LiteralPath $fixtures -Recurse -Force }
}
