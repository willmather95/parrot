[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')

$buildRoot = Join-Path $PSScriptRoot 'build'
$packageRoot = Join-Path $buildRoot ('package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $packageRoot
$packageFiles = @(
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

function Copy-PackageFile([string] $RelativePath, [string] $SourceRoot) {
    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains('\') -or
        $RelativePath.Contains(':') -or $RelativePath.Contains('//') -or $RelativePath.EndsWith('/') -or
        $RelativePath -match '(^|/)\.\.(/|$)') {
        throw "Unsafe package path: $RelativePath"
    }
    $windowsPath = $RelativePath.Replace('/', '\')
    $source = Join-Path $SourceRoot $windowsPath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Package input is missing: $source" }
    $destination = Join-Path $packageRoot $windowsPath
    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination))
    Copy-Item -LiteralPath $source -Destination $destination
}

try {
    foreach ($relative in $packageFiles) {
        if ($relative -in @('Install.ps1', 'README.md')) {
            Copy-PackageFile $relative $PSScriptRoot
        } else {
            Copy-PackageFile $relative $buildRoot
        }
    }

    $records = @()
    foreach ($relative in $packageFiles) {
        $file = Join-Path $packageRoot ($relative.Replace('/', '\'))
        $records += [ordered]@{
            path = $relative
            bytes = (Get-Item -LiteralPath $file).Length
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
        }
    }
    [ordered]@{
        product = 'Parrot.Windows'
        version = '0.2.0'
        schema = 2
        files = $records
    } | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $packageRoot 'package.json')

    $archive = Join-Path $buildRoot 'parrot-windows-x64-preview.zip'
    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    "$hash  parrot-windows-x64-preview.zip" | Set-Content -Encoding ASCII -LiteralPath "$archive.sha256"
    Write-Host "Packaged $archive"
} finally {
    # This directory was uniquely created by this invocation.
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
