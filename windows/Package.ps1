[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
$buildRoot = Join-Path $PSScriptRoot 'build'
$packageRoot = Join-Path $buildRoot ('package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $packageRoot
try {
    Copy-Item -LiteralPath (Join-Path $buildRoot 'Parrot.exe') -Destination $packageRoot
    foreach ($name in @('Install.ps1', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $packageRoot
    }
    $hashes = @{}
    foreach ($name in @('Parrot.exe', 'README.md', 'Install.ps1')) {
        $hashes[$name] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $packageRoot $name)).Hash.ToLowerInvariant()
    }
    @{ product = 'Parrot.Windows'; schema = 1; files = $hashes } | ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packageRoot 'package.json')
    $archive = Join-Path $buildRoot 'parrot-windows-x64-preview.zip'
    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    "$hash  parrot-windows-x64-preview.zip" | Set-Content -Encoding ASCII -LiteralPath "$archive.sha256"
    Write-Host "Packaged $archive"
} finally {
    # This directory was uniquely created by this invocation.
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
