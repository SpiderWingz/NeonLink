[CmdletBinding()]
param(
    [string]$BuildDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'Redline-build-081'),
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$project = Split-Path -Parent $PSScriptRoot
$destination = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $project 'Redline.exe' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
& (Join-Path $PSScriptRoot 'Build-Bundle.ps1') -BuildDirectory $BuildDirectory
$iconFile = Join-Path $BuildDirectory 'Redline.ico'
& (Join-Path $PSScriptRoot 'Build-Icon.ps1') -ImagePath (Join-Path $project 'assets/redline-logo.png') -Destination $iconFile
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    ('/out:' + $destination),
    ('/win32icon:' + $iconFile),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'NeonLink.exe.manifest')),
    ('/resource:' + $iconFile + ',Redline.Icon'),
    ('/resource:' + (Join-Path $project 'assets/redline-logo.png') + ',Redline.Logo.png'),
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
$arguments += Join-Path $PSScriptRoot 'ConnectionProgressTests.cs'
$arguments += Join-Path $PSScriptRoot 'CrashReports.cs'
$arguments += Join-Path $PSScriptRoot 'CrashReportTests.cs'
$arguments += Join-Path $PSScriptRoot 'BundledContent.cs'
$arguments += Join-Path $PSScriptRoot 'SetupTests.cs'
$arguments += Join-Path $PSScriptRoot 'InternetHosting.cs'
$arguments += Join-Path $PSScriptRoot 'DirectTests.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
Get-Item -LiteralPath $destination | Select-Object FullName, Length
