# Run on Windows: pwsh -STA -File design/icon/Generate-Icon.ps1
# Editable WPF vector source; no external image tools or packages required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src/WinMon/Assets'))
[IO.Directory]::CreateDirectory($assetDirectory) | Out-Null
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WinMon.xaml'))
$drawing = [Windows.Markup.XamlReader]::Parse($source)
# Optical correction: keep the waveform above one pixel at 16-24 px.
$smallDrawing = [Windows.Markup.XamlReader]::Parse($source.Replace('Thickness="14"', 'Thickness="20"'))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $frameDrawing = if ($size -le 24) { $smallDrawing } else { $drawing }
    $context.DrawImage($frameDrawing, [Windows.Rect]::new(0, 0, $size, $size))
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        $frames += ,$stream.ToArray()
    } finally { $stream.Dispose() }
}
$output = [IO.File]::Create((Join-Path $assetDirectory 'WinMon.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'WinMon.png'), $frames[-1])
Write-Output "Generated WinMon.ico ($($sizes -join ', ') px) and WinMon.png in $assetDirectory"
