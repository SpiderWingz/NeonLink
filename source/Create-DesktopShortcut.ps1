[CmdletBinding()]
param([string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Redline.exe'))
$ErrorActionPreference = 'Stop'
$target = (Resolve-Path -LiteralPath $Executable).Path
if ([IO.Path]::GetExtension($target) -ne '.exe') { throw 'Choose the built Redline executable.' }
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'Redline.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
try {
    if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.Description -ne 'Redline launcher for Cyberpunk 2077') {
        throw 'An unrelated Redline shortcut already exists on the desktop.'
    }
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = Split-Path -Parent $target
    $shortcut.IconLocation = $target + ',0'
    $shortcut.Description = 'Redline launcher for Cyberpunk 2077'
    $shortcut.Save()
} finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
}
Get-Item -LiteralPath $shortcutPath | Select-Object FullName,Length
