Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PSScriptRoot '..\macOS\Assets\AppIcon.png'
$outputPath = Join-Path $PSScriptRoot '..\CodexTempo\Assets\CodexTempo.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
$source = [Drawing.Image]::FromFile($sourcePath)

try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.DrawImage($source, 0, 0, $size, $size)

        $stream = [IO.MemoryStream]::new()
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())

        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
finally {
    $source.Dispose()
}

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
