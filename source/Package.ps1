[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
$release = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $release 'payload'
$relativeFiles = @('bin/x64/winmm.dll','red4ext/RED4ext.dll','red4ext/LICENSE.txt','red4ext/THIRD_PARTY_LICENSES.txt','red4ext/plugins/NeonLink/NeonLink.dll')
$relativeFiles += @(
    'red4ext/plugins/NeonLink/Scripts/NeonLink.reds',
    'red4ext/plugins/Codeware/Codeware.dll','red4ext/plugins/Codeware/LICENSE','red4ext/plugins/Codeware/THIRD_PARTY_LICENSES',
    'red4ext/plugins/Codeware/Data/KnownHashes.txt','red4ext/plugins/Codeware/Scripts/Codeware.Global.reds',
    'red4ext/plugins/Codeware/Scripts/Codeware.Localization.reds','red4ext/plugins/Codeware/Scripts/Codeware.reds',
    'red4ext/plugins/Codeware/Scripts/Codeware.UI.reds','red4ext/plugins/Codeware/Scripts/Codeware.UI.TextInput.reds',
    'engine/config/base/scripts.ini','engine/tools/scc_lib.dll','engine/tools/scc.exe','r6/config/cybercmd/scc.toml'
)
foreach ($relative in $relativeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) { throw "Missing payload file: $relative" }
}
$manifestPath = Join-Path $payload 'manifest.xml'
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($manifestPath, $settings)
try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement('NeonLinkPayload')
    $writer.WriteElementString('Version', '0.8.1-player-preview')
    $writer.WriteElementString('ClientVersion', '0.8.1-dev.1')
    $writer.WriteElementString('Red4extVersion', '1.31.0')
    $writer.WriteElementString('GameVersion', '2.5')
    $writer.WriteElementString('MultiplayerReady', 'false')
    $writer.WriteElementString('MovementPreview', 'true')
    $writer.WriteStartElement('Files')
    foreach ($relative in $relativeFiles) {
        $writer.WriteStartElement('File')
        $writer.WriteAttributeString('Path', $relative)
        $writer.WriteAttributeString('Sha256', (Get-FileHash -LiteralPath (Join-Path $payload $relative) -Algorithm SHA256).Hash.ToLowerInvariant())
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndDocument()
}
finally { $writer.Dispose() }

if (-not [string]::IsNullOrWhiteSpace($Destination)) {
    if (Test-Path -LiteralPath $Destination) { throw "Package already exists: $Destination" }
    $executable = Join-Path $release 'Redline.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Build Redline.exe before packaging a player release.' }
    $packageInputs = @('Redline.exe','README.txt','CREDITS.txt','RUNTIME-README.txt','CRASH-REPORTING.txt','TEST-RESULTS.txt','licenses') | ForEach-Object { Join-Path $release $_ }
    # Explicit inputs keep Git metadata, credentials, and development caches out.
    Compress-Archive -LiteralPath $packageInputs -DestinationPath $Destination
    Get-Item -LiteralPath $Destination | Select-Object Name,Length
}
