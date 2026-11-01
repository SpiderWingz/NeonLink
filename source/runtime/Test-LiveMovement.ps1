[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$GamePid,
    [Parameter(Mandatory)][string]$VerificationDirectory,
    [Parameter(Mandatory)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
$workRoot = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $workRoot) { throw 'Choose a fresh live-test directory.' }
$harness = Join-Path $VerificationDirectory 'RuntimeHarness.exe'
$dll = Join-Path $VerificationDirectory 'NeonLink.dll'
if (!(Test-Path -LiteralPath $harness) -or !(Test-Path -LiteralPath $dll)) { throw 'Run Test-Movement.ps1 to prepare the verified native fixture first.' }
New-Item -ItemType Directory -Path $workRoot | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$executable = Join-Path $workRoot 'LiveGameTests.exe'
$arguments = @('/nologo','/target:exe','/platform:x64',('/out:' + $executable),('/reference:' + (Join-Path $framework 'System.Web.Extensions.dll')),
    (Join-Path $PSScriptRoot 'LiveGameTests.cs'), (Join-Path (Split-Path -Parent $PSScriptRoot) 'RuntimeClient.cs'))
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Live fixture compilation failed.' }
& $executable $GamePid $harness $dll (Join-Path $workRoot 'session')
if ($LASTEXITCODE -ne 0) { throw 'Live movement test failed; inspect its output.' }
