# Build with the compiler and assemblies already included in Windows.
[CmdletBinding()]
param([switch] $SkipTests)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Build Parrot for Windows on Windows.' }
& (Join-Path $PSScriptRoot 'Get-Dependencies.ps1')
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework x64 compiler is unavailable.' }
$buildRoot = Join-Path $PSScriptRoot 'build'
$null = New-Item -ItemType Directory -Force -Path $buildRoot
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$sherpaAssembly = Join-Path $buildRoot 'sherpa-onnx.dll'
if (-not (Test-Path -LiteralPath $sherpaAssembly -PathType Leaf)) { throw 'The pinned sherpa-onnx managed assembly is unavailable.' }
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', $sherpaAssembly)
$references += @('UIAutomationClient.dll', 'UIAutomationTypes.dll', 'WindowsBase.dll' | ForEach-Object { Join-Path $framework "WPF\$_" })
$output = Join-Path $buildRoot 'Parrot.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warnaserror+', '/main:Parrot.Windows.Program', "/out:$output", "/win32manifest:$(Join-Path $PSScriptRoot 'src\app.manifest')")
$arguments += @($references | ForEach-Object { "/reference:$_" })
$arguments += $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE." }
if (-not $SkipTests) {
    $test = Start-Process -FilePath $output -ArgumentList '--self-test' -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Windows self-tests failed with exit code $($test.ExitCode)." }
    Write-Host 'PASS: Windows deterministic self-tests'
}
Write-Host "Built $output"
