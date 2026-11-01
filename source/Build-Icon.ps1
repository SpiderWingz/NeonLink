[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ImagePath,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$original = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($ImagePath))
$frames = @()
try {
    foreach ($size in @(16,24,32,48,64,128,256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $memory = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Black)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $ratio = [Math]::Min($size / [double]$original.Width, $size / [double]$original.Height)
            $width = [int][Math]::Round($original.Width * $ratio)
            $height = [int][Math]::Round($original.Height * $ratio)
            $graphics.DrawImage($original, [Drawing.Rectangle]::new([int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height))
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $frames += @{ Size = $size; Bytes = $memory.ToArray() }
        } finally { $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $original.Dispose() }
$writer = [IO.BinaryWriter]::new([IO.File]::Create([IO.Path]::GetFullPath($Destination)))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
