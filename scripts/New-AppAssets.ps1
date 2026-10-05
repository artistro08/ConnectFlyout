<#
    Outputs into src/ConnectFlyout.App/Assets: the MSIX logos at every scale and target size
    Windows asks for, and the two tray icons (TrayLight.ico for light taskbars, TrayDark.ico
    for dark ones).

    The logos come from AppIcon.ico, the app icon (also the exe's icon), which is kept as
    is. The tray icons draw the "Headphone" glyph (U+E7F6) from Segoe Fluent Icons, which
    ships with Windows 11. Uses System.Drawing: https://learn.microsoft.com/dotnet/api/system.drawing
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\ConnectFlyout.App\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$appIcon = (Resolve-Path (Join-Path $assets 'AppIcon.ico')).Path
$glyph = [string][char]0xE7F6
$fontFamily = 'Segoe Fluent Icons'

# Draw the app icon (Assets\AppIcon.ico) centered on a transparent canvas, at $Scale of the
# shorter side. Uses the icon's closest frame, so small sizes get its hand-sized images.
function New-AppIconImage {
    param(
        [int] $Width,
        [int] $Height,
        [double] $Scale,
        [string] $Path
    )

    $side = [int][Math]::Round([Math]::Min($Width, $Height) * $Scale)
    $icon = New-Object System.Drawing.Icon $appIcon, $side, $side
    $frame = $icon.ToBitmap()
    $icon.Dispose()

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($frame, [int](($Width - $side) / 2), [int](($Height - $side) / 2), $side, $side)
    $graphics.Dispose()
    $frame.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Output "Wrote $Path"
}

$white = [System.Drawing.Color]::White
$black = [System.Drawing.Color]::Black

# Package Logos, one file per size Windows asks for. The manifest names the plain file
# (e.g. Square44x44Logo.png) and Windows picks the qualified one that fits:
# https://learn.microsoft.com/windows/apps/design/style/iconography/app-icon-construction
# The plain files go away, since they'd clash with the scale-100 ones.
foreach ($name in 'Square44x44Logo', 'Square150x150Logo', 'Wide310x150Logo', 'StoreLogo') {
    $plain = Join-Path $assets "$name.png"
    if (Test-Path $plain) {
        Remove-Item $plain
    }
}

foreach ($scale in 100, 125, 150, 200, 400) {
    $factor = $scale / 100
    New-AppIconImage -Width ([int](44 * $factor)) -Height ([int](44 * $factor)) -Scale 1 -Path (Join-Path $assets "Square44x44Logo.scale-$scale.png")
    New-AppIconImage -Width ([int](150 * $factor)) -Height ([int](150 * $factor)) -Scale 0.6 -Path (Join-Path $assets "Square150x150Logo.scale-$scale.png")
    New-AppIconImage -Width ([int](310 * $factor)) -Height ([int](150 * $factor)) -Scale 0.6 -Path (Join-Path $assets "Wide310x150Logo.scale-$scale.png")
    New-AppIconImage -Width ([int](50 * $factor)) -Height ([int](50 * $factor)) -Scale 1 -Path (Join-Path $assets "StoreLogo.scale-$scale.png")
}

# Exact-size app icons (notifications, taskbar, Start's app list). The icon brings its own
# colors, so the plated and unplated (dark and light surface) versions are the same image.
foreach ($target in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    foreach ($suffix in '', '_altform-unplated', '_altform-lightunplated') {
        New-AppIconImage -Width $target -Height $target -Scale 1 -Path (Join-Path $assets "Square44x44Logo.targetsize-$target$suffix.png")
    }
}

# Draw the glyph at one exact pixel size as a classic icon image
function New-GlyphPng {
    param(
        [int] $Size,
        [System.Drawing.Color] $Foreground
    )

    # GDI+ draws this glyph blank at sizes off Segoe Fluent's 16 px grid (20, 24, 40, 48),
    # so draw it once at 64 px and shrink it to each size
    $master = New-Object System.Drawing.Bitmap 64, 64, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $masterGraphics = [System.Drawing.Graphics]::FromImage($master)
    $masterGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $masterGraphics.Clear([System.Drawing.Color]::Transparent)

    $font = New-Object System.Drawing.Font $fontFamily, 64, ([System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $area = New-Object System.Drawing.RectangleF 0, 0, 64, 64
    $masterGraphics.DrawString($glyph, $font, (New-Object System.Drawing.SolidBrush $Foreground), $area, $format)
    $masterGraphics.Dispose()

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($master, 0, 0, $Size, $Size)
    $master.Dispose()
    $graphics.Dispose()

    $image = ConvertTo-IconImage -Bitmap $bitmap
    $bitmap.Dispose()
    return , $image
}

# Classic icon image: BITMAPINFOHEADER (height doubled), 32-bit BGRA rows bottom-up, then
# the AND mask. Windows draws small PNG-compressed entries as blank.
function ConvertTo-IconImage {
    param(
        [System.Drawing.Bitmap] $Bitmap
    )

    $Size = $Bitmap.Width
    $bitmap = $Bitmap
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $maskStride = [int]([Math]::Ceiling($Size / 32.0) * 4)
    $writer.Write([uint32]40)
    $writer.Write([int32]$Size)
    $writer.Write([int32]($Size * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32](($Size * $Size * 4) + ($maskStride * $Size)))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)
    for ($y = $Size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $Size; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B)
            $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R)
            $writer.Write([byte]$pixel.A)
        }
    }
    # AND mask: 1 = transparent. Set it wherever alpha is 0 so the background stays
    # transparent even where Windows ignores the alpha channel (the tray did).
    for ($y = $Size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $Size; $x++) {
            if ($bitmap.GetPixel($x, $y).A -eq 0) {
                $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
            }
        }
        $writer.Write($row)
    }
    $writer.Flush()

    return , $stream.ToArray()
}

# Write a .ico holding one image per size
function New-GlyphIcon {
    param(
        [System.Drawing.Color] $Foreground,
        [string] $Path
    )

    $sizes = 16, 20, 24, 32, 40, 48, 64
    $images = foreach ($size in $sizes) { , (New-GlyphPng -Size $size -Foreground $Foreground) }
    Write-IconFile -Sizes $sizes -Images $images -Path $Path
}

# Write the icon directory and images; a 256 entry is stored as its size 0
function Write-IconFile {
    param(
        [int[]] $Sizes,
        [object[]] $Images,
        [string] $Path
    )

    $sizes = $Sizes
    $images = $Images
    $file = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter $file
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $writer.Write([byte]($sizes[$i] % 256))
        $writer.Write([byte]($sizes[$i] % 256))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) {
        $writer.Write([byte[]]$image)
    }
    $writer.Dispose()
    Write-Output "Wrote $Path"
}

# Tray Icons
New-GlyphIcon -Foreground $black -Path (Join-Path $assets 'TrayLight.ico')
New-GlyphIcon -Foreground $white -Path (Join-Path $assets 'TrayDark.ico')
