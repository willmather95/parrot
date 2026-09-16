# Download and verify the pinned local inference dependencies into windows/build.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($env:OS -ne 'Windows_NT') { throw 'Acquire Windows dependencies on Windows.' }

$manifestPath = Join-Path $PSScriptRoot 'dependencies.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1) { throw 'Unrecognized dependency manifest.' }

$buildRoot = Join-Path $PSScriptRoot 'build'
$downloadRoot = Join-Path $buildRoot 'downloads'
$null = New-Item -ItemType Directory -Force -Path $downloadRoot

function Assert-RelativePath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path) -or
        $Path.Contains('\') -or $Path.Contains(':') -or $Path.Contains('//') -or $Path.EndsWith('/') -or
        $Path -match '(^|/)\.\.(/|$)' -or
        $Path -match '(^|/)\.(/|$)') {
        throw "Unsafe dependency path: $Path"
    }
}

function Assert-File([string] $Path, [long] $Bytes, [string] $Sha256) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing dependency file: $Path" }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Length -ne $Bytes) { throw "Unexpected size for $Path. Expected $Bytes bytes, found $($item.Length)." }
    if ($Sha256 -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid pinned checksum for $Path." }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $Sha256) { throw "Checksum mismatch for $Path." }
}

function Receive-PinnedFile([string] $Url, [string] $Destination, [long] $Bytes, [string] $Sha256) {
    $uri = [Uri] $Url
    if ($uri.Scheme -cne 'https') { throw "Dependency downloads require HTTPS: $Url" }
    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        try {
            Assert-File $Destination $Bytes $Sha256
            return
        } catch {
            Write-Host "Replacing invalid dependency cache file $Destination"
        }
    }
    $parent = [IO.Path]::GetDirectoryName($Destination)
    $null = New-Item -ItemType Directory -Force -Path $parent
    $temporary = $Destination + '.download-' + [Guid]::NewGuid().ToString('N')
    try {
        $downloaded = $false
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
                Write-Host "Downloading $Url (attempt $attempt of 3)"
                Invoke-WebRequest -Uri $uri -OutFile $temporary -UseBasicParsing -TimeoutSec 600
                Assert-File $temporary $Bytes $Sha256
                $downloaded = $true
                break
            } catch {
                if ($attempt -eq 3) { throw }
                Write-Host "Dependency download attempt $attempt failed; retrying."
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
        if (-not $downloaded) { throw "Dependency download did not complete: $Url" }
        Move-Item -LiteralPath $temporary -Destination $Destination -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Copy-PinnedZipEntry(
    [string] $Archive,
    [string] $ArchivePath,
    [string] $Destination,
    [long] $Bytes,
    [string] $Sha256
) {
    Assert-RelativePath $ArchivePath
    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        try {
            Assert-File $Destination $Bytes $Sha256
            return
        } catch {
            Write-Host "Replacing invalid extracted dependency $Destination"
        }
    }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $matches = @($zip.Entries | Where-Object { $_.FullName -ceq $ArchivePath })
        if ($matches.Count -ne 1) { throw "Expected exactly one archive entry named $ArchivePath." }
        if ($matches[0].Length -ne $Bytes) { throw "Unexpected uncompressed size for $ArchivePath." }
        $parent = [IO.Path]::GetDirectoryName($Destination)
        $null = New-Item -ItemType Directory -Force -Path $parent
        $temporary = $Destination + '.extract-' + [Guid]::NewGuid().ToString('N')
        try {
            $input = $matches[0].Open()
            try {
                $output = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            } finally { $input.Dispose() }
            Assert-File $temporary $Bytes $Sha256
            Move-Item -LiteralPath $temporary -Destination $Destination -Force
        } finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    } finally { $zip.Dispose() }
}

# Windows PowerShell on older supported systems may not default to TLS 1.2.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$outputs = @{}
foreach ($artifact in @($manifest.artifacts)) {
    if ([string]::IsNullOrWhiteSpace([string] $artifact.url)) { throw 'Dependency URL is missing.' }
    if ($artifact.type -eq 'zip') {
        Assert-RelativePath ([string] $artifact.cacheFile)
        $archive = Join-Path $downloadRoot ([string] $artifact.cacheFile)
        Receive-PinnedFile ([string] $artifact.url) $archive ([long] $artifact.bytes) ([string] $artifact.sha256)
        foreach ($entry in @($artifact.entries)) {
            $relative = [string] $entry.outputPath
            Assert-RelativePath $relative
            if ($outputs.ContainsKey($relative.ToLowerInvariant())) { throw "Duplicate dependency output: $relative" }
            $outputs[$relative.ToLowerInvariant()] = $true
            Copy-PinnedZipEntry $archive ([string] $entry.archivePath) (Join-Path $buildRoot ($relative.Replace('/', '\'))) ([long] $entry.bytes) ([string] $entry.sha256)
        }
    } elseif ($artifact.type -eq 'file') {
        $relative = [string] $artifact.outputPath
        Assert-RelativePath $relative
        if ($outputs.ContainsKey($relative.ToLowerInvariant())) { throw "Duplicate dependency output: $relative" }
        $outputs[$relative.ToLowerInvariant()] = $true
        Receive-PinnedFile ([string] $artifact.url) (Join-Path $buildRoot ($relative.Replace('/', '\'))) ([long] $artifact.bytes) ([string] $artifact.sha256)
    } else {
        throw "Unsupported dependency artifact type: $($artifact.type)"
    }
}

$notice = @'
Parrot for Windows third-party notices

sherpa-onnx 1.13.8
Copyright 2019-2023 Xiaomi Corporation and the Next-gen Kaldi development team.
Licensed under Apache License 2.0. See licenses/LICENSE-sherpa-onnx.txt.
Source: https://github.com/k2-fsa/sherpa-onnx/tree/dc5583f49917e4c95f6e7d862bb378e4ed5e9076

ONNX Runtime 1.28.2
Copyright Microsoft Corporation.
Licensed under the MIT License. See licenses/LICENSE-onnxruntime.txt and licenses/NOTICE-onnxruntime-third-party.txt.
Source: https://github.com/microsoft/onnxruntime/tree/v1.28.2

Parakeet TDT 0.6b v2 INT8 ONNX model
Published by csukuangfj as a sherpa-onnx-compatible INT8 conversion of NVIDIA Parakeet TDT 0.6b v2.
The original NVIDIA model is https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2; this package uses the converted and INT8-quantized ONNX files linked below.
Licensed under Creative Commons Attribution 4.0 International. See licenses/LICENSE-parakeet-model.txt.
Source: https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/tree/1ab9323565ddb038682214b292f588070a538ce2
'@
$noticePath = Join-Path $buildRoot 'THIRD-PARTY-NOTICES.txt'
[IO.File]::WriteAllText($noticePath, $notice.TrimStart() + "`r`n", [Text.Encoding]::UTF8)

Write-Host "Verified pinned dependencies in $buildRoot"
