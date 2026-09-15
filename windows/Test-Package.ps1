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
$null = New-Item -ItemType Directory -Path $scratch
$created = $false
try {
    $archive = Join-Path $PSScriptRoot 'build\parrot-windows-x64-preview.zip'
    $expected = (Get-Content -LiteralPath "$archive.sha256" -Raw).Split(' ')[0]
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant() -cne $expected) { throw 'Archive hash mismatch.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $scratch
    & (Join-Path $scratch 'Install.ps1')
    $created = $true
    $before = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination 'Parrot.exe')).Hash
    $process = Start-Process -FilePath (Join-Path $destination 'Parrot.exe') -ArgumentList '--self-test' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Installed binary self-test failed.' }
    # A valid replacement remains installable.
    & (Join-Path $scratch 'Install.ps1')
    $previousFiles = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $destination -File)) {
        $previousFiles[$file.Name] = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    }
    # Inject a fault into a disposable copy AFTER promotion, forcing the actual
    # installer's catch/rollback path. No fault hooks ship in the real installer.
    $installer = Join-Path $scratch 'Install.ps1'
    $originalInstaller = Get-Content -LiteralPath $installer -Raw
    $needle = '    $installed = $true'
    if (-not $originalInstaller.Contains($needle)) { throw 'Rollback fault-injection anchor changed.' }
    $injected = $originalInstaller.Replace($needle, $needle + "`r`n    throw 'Injected post-promotion failure'")
    Set-Content -LiteralPath $installer -Encoding UTF8 -Value $injected
    $manifestPath = Join-Path $scratch 'package.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.files.'Install.ps1' = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $rolledBack = $false
    try { & $installer } catch {
        if ($_.Exception.Message -notlike '*Injected post-promotion failure*') { throw }
        $rolledBack = $true
    }
    if (-not $rolledBack) { throw 'Injected update failure did not fail.' }
    if (@(Get-ChildItem -LiteralPath $destination -File).Count -ne $previousFiles.Count) { throw 'Rollback changed the installed file set.' }
    foreach ($name in $previousFiles.Keys) {
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination $name)).Hash -cne $previousFiles[$name]) {
            throw "Rollback did not restore $name."
        }
    }
    # Restore the original extracted package without its injected failure.
    Expand-Archive -LiteralPath $archive -DestinationPath $scratch -Force
    Add-Content -LiteralPath $installer -Value '# corrupted installer fixture'
    $rejected = $false
    try { & $installer } catch { $rejected = $true }
    if (-not $rejected) { throw 'Corrupt installer was accepted.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $scratch -Force
    # A corrupted download must fail before changing the existing installation.
    Add-Content -LiteralPath (Join-Path $scratch 'README.md') -Value 'corrupted fixture'
    $rejected = $false
    try { & (Join-Path $scratch 'Install.ps1') } catch { $rejected = $true }
    if (-not $rejected) { throw 'Corrupt package was accepted.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination 'Parrot.exe')).Hash -cne $before) { throw 'Corrupt update changed the installed binary.' }
    Write-Host 'PASS: package checksums, installation, installed self-test, update, forced rollback, and corrupt-update preservation'
} finally {
    if ($created -and (Test-Path -LiteralPath $destination)) { Remove-Item -LiteralPath $destination -Recurse -Force }
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
