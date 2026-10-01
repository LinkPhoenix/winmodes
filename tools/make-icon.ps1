#Requires -Version 7.0
<#
.SYNOPSIS
    Generates the application icon (rounded gradient square with a lightning glyph) as a multi-size .ico.
.DESCRIPTION
    Uses the Segoe Fluent Icons font shipped with Windows 11. Run again only to change the design.
#>
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/WinModes.App/Assets/winmodes.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Sizes = 256, 64, 48, 32, 24, 16
$LightningGlyph = [string][char]0xE945
$CornerRatio = 0.22
$GlyphRatio = 0.56

function New-IconPng([int]$Size) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = 'AntiAlias'
        $graphics.TextRenderingHint = 'AntiAliasGridFit'
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $radius = [float]($Size * $CornerRatio)
        $diameter = $radius * 2
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($Size - $diameter - 1, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($Size - $diameter - 1, $Size - $diameter - 1, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $Size - $diameter - 1, $diameter, $diameter, 90, 90)
        $path.CloseFigure()

        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Point]::new(0, 0), [System.Drawing.Point]::new($Size, $Size),
            [System.Drawing.Color]::FromArgb(0x7C, 0x5C, 0xFC), [System.Drawing.Color]::FromArgb(0xEC, 0x48, 0x99))
        $graphics.FillPath($brush, $path)

        $font = [System.Drawing.Font]::new('Segoe Fluent Icons', [float]($Size * $GlyphRatio), [System.Drawing.GraphicsUnit]::Pixel)
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = 'Center'
        $format.LineAlignment = 'Center'
        $graphics.DrawString($LightningGlyph, $font, [System.Drawing.Brushes]::White,
            [System.Drawing.RectangleF]::new(0, [float]($Size * 0.03), $Size, $Size), $format)

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$images = foreach ($size in $Sizes) { [pscustomobject]@{ Size = $size; Bytes = (New-IconPng $size) } }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    # ICONDIR, then one ICONDIRENTRY per image, then the PNG payloads.
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        # 0 means 256 in the one-byte width and height fields.
        $dimension = [byte]($image.Size -eq 256 ? 0 : $image.Size)
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
"Saved $OutputPath"
