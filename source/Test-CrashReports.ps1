[CmdletBinding()]
param([string]$TestDirectory = (Join-Path $env:TEMP ('NeonLink-crash-tests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$client = Join-Path (Split-Path -Parent $PSScriptRoot) 'NeonLink.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $TestDirectory | Out-Null
$fixture = Join-Path $TestDirectory 'CrashFixture.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $fixture) (Join-Path $PSScriptRoot 'tests\CrashFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
$root = Join-Path $TestDirectory 'reports'
$process = Start-Process -FilePath $client -ArgumentList @('--test-crashes', ('"' + $root + '"'), ('"' + $fixture + '"')) -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw 'Crash-report tests failed. See the local launcher log for details.' }
Get-Content -LiteralPath (Join-Path $root 'result.txt')
