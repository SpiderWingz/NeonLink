[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Zig,
    [Parameter(Mandatory)][string]$SodiumRoot,
    [string]$CacheDirectory = (Join-Path $env:TEMP 'NeonLink-native-cache'),
    [string]$OutputPath,
    [string]$SdkRoot
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$destination = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $project 'artifacts\movement\NeonLink.dll' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $CacheDirectory 'global'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $CacheDirectory 'local'
$binaryDirectory = Join-Path $CacheDirectory 'artifacts'
New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null
$builtDll = Join-Path $binaryDirectory 'NeonLink.dll'
$gameplayArguments = @()
if ($SdkRoot) {
    $sdkHeaders = Join-Path $SdkRoot 'include'
    if (-not (Test-Path (Join-Path $sdkHeaders 'RED4ext/RED4ext.hpp'))) { throw 'Complete RED4ext SDK is required for the game bridge.' }
    $callbackHeader = Get-Content -LiteralPath (Join-Path $sdkHeaders 'RED4ext/Callback.hpp') -Raw
    if ($callbackHeader -notmatch 'DefaultFixedCallbackBufferSize = 64') { throw 'SDK must include the Cyberpunk 2.5 callback ABI update.' }
    $gameplayArguments = @('-DNEONLINK_ENGINE_BRIDGE', '-fms-extensions', '-Wno-ignored-attributes', '-Wno-unused-value', '-Wno-dynamic-class-memaccess', '-Wno-nonportable-include-path', '-Wno-c++23-extensions', '-Wno-deprecated-volatile', '-Wno-invalid-offsetof', ('-I' + $sdkHeaders), ('-I' + (Join-Path $SdkRoot 'vendor/D3D12MemAlloc')), (Join-Path $PSScriptRoot 'GameBridge.cpp'))
}
& $Zig c++ -target x86_64-windows-gnu -std=c++20 -O2 -shared `
    @gameplayArguments `
    ('-I' + (Join-Path $PSScriptRoot 'vendor')) `
    ('-I' + (Join-Path $SodiumRoot 'include')) `
    (Join-Path $PSScriptRoot 'Runtime.cpp') (Join-Path $SodiumRoot 'lib\libsodium.a') `
    -lws2_32 -ladvapi32 -lbcrypt -lshell32 -o $builtDll
if ($LASTEXITCODE -ne 0) { throw 'Native runtime compilation failed.' }
# Check the artifact is an x64 Windows DLL rather than an object archive.
$stream = [IO.File]::OpenRead($builtDll)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Compiler did not emit a Windows PE file.' }
    $stream.Position = 0x3c; $peOffset = $reader.ReadInt32()
    $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Compiler did not emit an x64 PE file.' }
    $stream.Position = $peOffset + 22
    if (($reader.ReadUInt16() -band 0x2000) -eq 0) { throw 'The PE file is not a DLL.' }
} finally { $reader.Dispose(); $stream.Dispose() }
Copy-Item -LiteralPath $builtDll -Destination $destination -Force
Get-Item -LiteralPath $destination | Select-Object FullName, Length
