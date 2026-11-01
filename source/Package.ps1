[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
$release = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $release 'payload'
$relativeFiles = @('bin/x64/winmm.dll','red4ext/RED4ext.dll','red4ext/LICENSE.txt','red4ext/THIRD_PARTY_LICENSES.txt','red4ext/plugins/NeonLink/NeonLink.dll')
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
    $writer.WriteElementString('Version', '0.7.0-direct-hosting-preview')
    $writer.WriteElementString('ClientVersion', '0.7.0')
    $writer.WriteElementString('Red4extVersion', '1.31.0')
    $writer.WriteElementString('GameVersion', '2.5')
    $writer.WriteElementString('MultiplayerReady', 'false')
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
    $executable = Join-Path $release 'NeonLink.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Build NeonLink.exe before packaging a player release.' }
    $packageInputs = @('NeonLink.exe','README.txt','CREDITS.txt','RUNTIME-README.txt','CRASH-REPORTING.txt','TEST-RESULTS.txt','licenses') | ForEach-Object { Join-Path $release $_ }
    # Explicit inputs keep Git metadata, credentials, and development caches out.
    Compress-Archive -LiteralPath $packageInputs -DestinationPath $Destination
    Get-Item -LiteralPath $Destination | Select-Object Name,Length
}
