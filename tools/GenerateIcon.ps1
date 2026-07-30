Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)

    $scale = $size / 64.0
    $background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 72, 105, 79))
    $leaf = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 238, 244, 234))
    $vein = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255, 96, 122, 102), [Math]::Max(1.0, 3.0 * $scale))
    $vein.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $vein.EndCap = [Drawing.Drawing2D.LineCap]::Round

    $graphics.FillEllipse($background, 2 * $scale, 2 * $scale, 60 * $scale, 60 * $scale)
    $state = $graphics.Save()
    $graphics.TranslateTransform(32 * $scale, 31 * $scale)
    $graphics.RotateTransform(32)
    $graphics.FillEllipse($leaf, -10 * $scale, -20 * $scale, 20 * $scale, 40 * $scale)
    $graphics.Restore($state)
    $graphics.DrawLine($vein, 20 * $scale, 46 * $scale, 45 * $scale, 17 * $scale)

    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())

    $stream.Dispose()
    $vein.Dispose()
    $leaf.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$outputPath = Join-Path $PSScriptRoot '..\CodexTempo\Assets\CodexTempo.ico'
$outputDirectory = Split-Path -Parent $outputPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$output = [IO.File]::Create($outputPath)
$writer = [IO.BinaryWriter]::new($output)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([Byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$index].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$index].Length
}

foreach ($image in $images) {
    $writer.Write($image)
}

$writer.Dispose()
$output.Dispose()
