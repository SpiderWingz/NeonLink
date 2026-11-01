[CmdletBinding()]
param([string]$BuildDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'NeonLink-build-071'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$project = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Bundle.ps1') -BuildDirectory $BuildDirectory
# Generate the app's own small N mark; no downloaded icon assets are needed.
Add-Type -AssemblyName System.Drawing
$iconFile = Join-Path $PSScriptRoot 'NeonLink.ico'
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(17,23,33))
$accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(56,215,242))
$ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(10,21,31))
$graphics.FillRectangle($accent, 5, 5, 54, 54)
$font = New-Object System.Drawing.Font 'Segoe UI',38,([System.Drawing.FontStyle]::Bold -bor [System.Drawing.FontStyle]::Italic),([System.Drawing.GraphicsUnit]::Pixel)
$graphics.DrawString('N', $font, $ink, 14, 7)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconStream = [System.IO.File]::Create($iconFile)
try { $icon.Save($iconStream) } finally { $iconStream.Dispose(); $icon.Dispose(); $font.Dispose(); $accent.Dispose(); $ink.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    ('/out:' + (Join-Path $project 'NeonLink.exe')),
    ('/win32icon:' + $iconFile),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'NeonLink.exe.manifest')),
    ('/resource:' + (Join-Path $PSScriptRoot 'Shell.xaml') + ',NeonLink.Shell.xaml'),
    ('/resource:' + (Join-Path $BuildDirectory 'NeonLink.Bundle.zip') + ',NeonLink.Bundle.zip'),
    ('/resource:' + (Join-Path $BuildDirectory 'NeonLink.Bundle.sha256') + ',NeonLink.Bundle.sha256'),
    ('/resource:' + (Join-Path $project 'assets\night-city.jpg') + ',NeonLink.night-city.jpg'),
    ('/resource:' + (Join-Path $project 'assets\rooftop.jpg') + ',NeonLink.rooftop.jpg'),
    ('/resource:' + (Join-Path $project 'assets\street.jpg') + ',NeonLink.street.jpg')
)
foreach ($reference in @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) {
    $arguments += '/reference:' + (Join-Path $framework $reference)
}
$arguments += Join-Path $PSScriptRoot 'Launcher.cs'
$arguments += Join-Path $PSScriptRoot 'RuntimeClient.cs'
$arguments += Join-Path $PSScriptRoot 'CrashReports.cs'
$arguments += Join-Path $PSScriptRoot 'CrashReportTests.cs'
$arguments += Join-Path $PSScriptRoot 'BundledContent.cs'
$arguments += Join-Path $PSScriptRoot 'SetupTests.cs'
$arguments += Join-Path $PSScriptRoot 'InternetHosting.cs'
$arguments += Join-Path $PSScriptRoot 'DirectTests.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
Get-Item -LiteralPath (Join-Path $project 'NeonLink.exe') | Select-Object FullName, Length
