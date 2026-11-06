# Render Assets/logo.xaml và Assets/banner.xaml ra PNG + app.ico.
# Chạy: powershell -STA -ExecutionPolicy Bypass -File tools\render-logo.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$assets = Join-Path $PSScriptRoot '..\Assets' | Resolve-Path

function Load-Xaml([string]$file) {
    [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $assets $file)))
}

function Render-Png($element, [int]$w, [int]$h) {
    $element.Width = $w; $element.Height = $h
    $element.Measure([Windows.Size]::new($w, $h))
    $element.Arrange([Windows.Rect]::new(0, 0, $w, $h))
    $element.UpdateLayout()
    $rtb = [Windows.Media.Imaging.RenderTargetBitmap]::new($w, $h, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($element)
    $enc = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = [IO.MemoryStream]::new()
    $enc.Save($ms)
    , $ms.ToArray()
}

# PNG các cỡ cho icon
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @{}
foreach ($s in $sizes) { $frames[$s] = Render-Png (Load-Xaml 'logo.xaml') $s $s }
[IO.File]::WriteAllBytes((Join-Path $assets 'logo-256.png'), $frames[256])
[IO.File]::WriteAllBytes((Join-Path $assets 'logo-512.png'), (Render-Png (Load-Xaml 'logo.xaml') 512 512))

# ICO chứa các frame PNG (Windows Vista trở lên đọc được)
$ico = [IO.MemoryStream]::new()
$bw = [IO.BinaryWriter]::new($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $b = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write($b); $bw.Write($b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$frames[$s].Length); $bw.Write([uint32]$offset)
    $offset += $frames[$s].Length
}
foreach ($s in $sizes) { $bw.Write($frames[$s]) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $ico.ToArray())

# Banner README
$banner = Load-Xaml 'banner.xaml'
$banner.FindName('LogoHost').Content = Load-Xaml 'logo.xaml'
[IO.File]::WriteAllBytes((Join-Path $assets 'banner.png'), (Render-Png $banner 1280 400))

# Ảnh wizard cho Inno Setup (BMP 24-bit, nền đặc)
function Save-Bmp($element, [int]$w, [int]$h, [string]$name) {
    $png = Render-Png $element $w $h
    $src = [Windows.Media.Imaging.BitmapDecoder]::Create([IO.MemoryStream]::new($png),
        [Windows.Media.Imaging.BitmapCreateOptions]::None, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad).Frames[0]
    $bgr = [Windows.Media.Imaging.FormatConvertedBitmap]::new($src, [Windows.Media.PixelFormats]::Bgr24, $null, 0)
    $enc = [Windows.Media.Imaging.BmpBitmapEncoder]::new()
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bgr))
    $fs = [IO.File]::Create((Join-Path $assets $name)); $enc.Save($fs); $fs.Close()
}
$wizard = Load-Xaml 'wizard.xaml'
$wizard.FindName('LogoHost').Content = Load-Xaml 'logo.xaml'
Save-Bmp $wizard 328 628 'wizard-large.bmp'
$small = [Windows.Controls.Border]::new()
$small.Background = [Windows.Media.Brushes]::White
$small.Padding = [Windows.Thickness]::new(6)
$small.Child = Load-Xaml 'logo.xaml'
Save-Bmp $small 110 110 'wizard-small.bmp'

"Exported: wizard-large.bmp, wizard-small.bmp, app.ico ($($sizes -join ', ') px), logo-256.png, logo-512.png, banner.png"
