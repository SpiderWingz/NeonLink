[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Zig,
    [Parameter(Mandatory)][string]$SodiumRoot,
    [Parameter(Mandatory)][string]$SdkRoot,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [string]$CacheDirectory = (Join-Path $env:TEMP 'NeonLink-native-cache'),
    [string]$GameBundle,
    [string]$CodewareScripts,
    [string]$RedscriptCli
)
$ErrorActionPreference = 'Stop'
$workRoot = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $workRoot) { throw 'Choose a fresh work directory for isolated fixtures.' }
New-Item -ItemType Directory -Path $workRoot | Out-Null
$dll = Join-Path $workRoot 'NeonLink.dll'
& (Join-Path $PSScriptRoot 'Build-Runtime.ps1') -Zig $Zig -SodiumRoot $SodiumRoot -SdkRoot $SdkRoot -CacheDirectory $CacheDirectory -OutputPath $dll
$unitTests = Join-Path $workRoot 'ReplicationTests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 (Join-Path $PSScriptRoot 'ReplicationTests.cpp') -o $unitTests
if ($LASTEXITCODE -ne 0) { throw 'Movement protocol test compilation failed.' }
& $unitTests
if ($LASTEXITCODE -ne 0) { throw 'Movement protocol tests failed.' }
$appearanceTests = Join-Path $workRoot 'AppearanceTests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 (Join-Path $PSScriptRoot 'AppearanceTests.cpp') -o $appearanceTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance protocol test compilation failed.' }
& $appearanceTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance protocol tests failed.' }
$appearanceV2Tests = Join-Path $workRoot 'AppearanceV2Tests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 (Join-Path $PSScriptRoot 'AppearanceV2Tests.cpp') -o $appearanceV2Tests
if ($LASTEXITCODE -ne 0) { throw 'Appearance v2 test compilation failed.' }
& $appearanceV2Tests
if ($LASTEXITCODE -ne 0) { throw 'Appearance v2 tests failed.' }
$appearanceFragmentTests = Join-Path $workRoot 'AppearanceFragmentsTests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 ('-I' + (Join-Path $SodiumRoot 'include')) (Join-Path $PSScriptRoot 'AppearanceFragmentsTests.cpp') -o $appearanceFragmentTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance fragment test compilation failed.' }
& $appearanceFragmentTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance fragment tests failed.' }
$appearancePolicyTests = Join-Path $workRoot 'AppearancePolicyTests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 (Join-Path $PSScriptRoot 'AppearancePolicyTests.cpp') -o $appearancePolicyTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance policy test compilation failed.' }
& $appearancePolicyTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance policy tests failed.' }
$appearanceStagingTests = Join-Path $workRoot 'AppearanceStagingTests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 (Join-Path $PSScriptRoot 'AppearanceStagingTests.cpp') -o $appearanceStagingTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance staging test compilation failed.' }
& $appearanceStagingTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance staging tests failed.' }
$appearanceCatalogTests = Join-Path $workRoot 'AppearanceCatalogBuilderTests.exe'
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 (Join-Path $PSScriptRoot 'AppearanceCatalogBuilderTests.cpp') -o $appearanceCatalogTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance catalog test compilation failed.' }
& $appearanceCatalogTests
if ($LASTEXITCODE -ne 0) { throw 'Appearance catalog tests failed.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$harness = Join-Path $workRoot 'RuntimeHarness.exe'
$arguments = @('/nologo','/target:exe','/platform:x64','/optimize+',('/out:' + $harness),('/reference:' + (Join-Path $framework 'System.Web.Extensions.dll')),
    (Join-Path $PSScriptRoot 'Harness.cs'), (Join-Path $PSScriptRoot 'MovementIntegrationTests.cs'), (Join-Path $PSScriptRoot 'TransportTestEnvironment.cs'), (Join-Path (Split-Path -Parent $PSScriptRoot) 'RuntimeClient.cs'),
    (Join-Path (Split-Path -Parent $PSScriptRoot) 'DirectTests.cs'), (Join-Path (Split-Path -Parent $PSScriptRoot) 'InternetHosting.cs'))
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native harness compilation failed.' }
& $harness --test-movement $dll (Join-Path $workRoot 'movement-fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Native movement integration tests failed.' }
& $harness --test $dll (Join-Path $workRoot 'transport-fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Native transport regression tests failed.' }
& $harness --test-direct $dll (Join-Path $workRoot 'direct-fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Direct hosting regression tests failed.' }
if ($GameBundle -or $CodewareScripts -or $RedscriptCli) {
    if (!$GameBundle -or !$CodewareScripts -or !$RedscriptCli) { throw 'Script validation requires GameBundle, CodewareScripts, and RedscriptCli together.' }
    $validationBundle = Join-Path $workRoot 'validation-only.redscripts'
    & $RedscriptCli compile -s (Join-Path $PSScriptRoot 'scripts') -s $CodewareScripts -b $GameBundle -o $validationBundle
    # This CLI can return zero after reporting a compiler error. A fresh output
    # bundle is required in addition to its process exit status.
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $validationBundle -PathType Leaf)) { throw 'Gameplay script validation failed.' }
    # The compiled bundle contains game scripts and is never a release input.
}
Write-Output ('Movement development checks passed. Artifacts: ' + $workRoot)
