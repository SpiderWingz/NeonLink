[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$release = Split-Path -Parent $PSScriptRoot
[System.IO.Directory]::CreateDirectory($BuildDirectory) | Out-Null
& (Join-Path $PSScriptRoot 'Package.ps1')
Copy-Item -LiteralPath (Join-Path $release 'payload/red4ext/LICENSE.txt') -Destination (Join-Path $release 'licenses/RED4ext-MIT.txt')
Copy-Item -LiteralPath (Join-Path $release 'payload/red4ext/THIRD_PARTY_LICENSES.txt') -Destination (Join-Path $release 'licenses/RED4ext-THIRD-PARTY.txt')
$files = @('README.txt','RUNTIME-README.txt','CRASH-REPORTING.txt','CREDITS.txt')
foreach ($folder in @('payload','assets/backdrops','licenses')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $release $folder) -File -Recurse | Sort-Object FullName)) {
        $files += $file.FullName.Substring($release.Length + 1).Replace('\','/')
    }
}
$indexPath = Join-Path $BuildDirectory 'bundle-index.xml'
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($indexPath, $settings)
try {
    $writer.WriteStartDocument(); $writer.WriteStartElement('NeonLinkBundle'); $writer.WriteAttributeString('Version','0.7.0')
    foreach ($relative in $files) {
        $path = Join-Path $release $relative
        $writer.WriteStartElement('File')
        $writer.WriteAttributeString('Path',$relative)
        $writer.WriteAttributeString('Length',(Get-Item -LiteralPath $path).Length.ToString([System.Globalization.CultureInfo]::InvariantCulture))
        $writer.WriteAttributeString('Sha256',(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement(); $writer.WriteEndDocument()
} finally { $writer.Dispose() }
$bundle = Join-Path $BuildDirectory 'NeonLink.Bundle.zip'
$stream = [System.IO.File]::Create($bundle)
$archive = [System.IO.Compression.ZipArchive]::new($stream,[System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $release $relative),$relative,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$indexPath,'bundle-index.xml',[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $archive.Dispose(); $stream.Dispose() }
[System.IO.File]::WriteAllText((Join-Path $BuildDirectory 'NeonLink.Bundle.sha256'),(Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant(),[System.Text.Encoding]::ASCII)
