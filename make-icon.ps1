# Generates app.ico for Prisma: a dark rounded tile with a bold "P" monogram filled
# with a teal -> blue -> violet -> magenta -> pink prism-spectrum gradient. PNG-compressed multi-size ICO
# (16..256), valid on Vista+. The P is built as a glyph PATH (not DrawString) so it
# stays crisp and precisely centred at every size, including the 16px tray icon.
Add-Type -AssemblyName System.Drawing

function DrawIconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # rounded dark tile with a subtle top-to-bottom gradient for depth
    $radius = [Math]::Max(2, [int]($size * 0.22))
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $tileRect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $tile = New-Object System.Drawing.Drawing2D.LinearGradientBrush($tileRect, [System.Drawing.Color]::FromArgb(255, 28, 31, 41), [System.Drawing.Color]::FromArgb(255, 15, 16, 23), [single]90)
    $g.FillPath($tile, $path)
    $tile.Dispose()
    # faint rim so the tile still reads on a dark taskbar
    $rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(26, 255, 255, 255), [single][Math]::Max(1.0, $size * 0.016))
    $g.DrawPath($rim, $path)
    $rim.Dispose()
    $path.Dispose()

    # bold "P" as a glyph path
    $cx = $size / 2.0
    $cy = $size / 2.0
    $em = [single]($size * 0.92)
    $glyph = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family = New-Object System.Drawing.FontFamily("Arial")
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $glyph.AddString("P", $family, [int][System.Drawing.FontStyle]::Bold, $em, (New-Object System.Drawing.PointF(0, 0)), $fmt)
    $family.Dispose()

    # centre it in the tile (nudge up a hair so it sits optically centred)
    $gb = $glyph.GetBounds()
    $tx = [single]($cx - ($gb.X + $gb.Width / 2.0))
    $ty = [single]($cy - ($gb.Y + $gb.Height / 2.0) - $size * 0.01)
    $mat = New-Object System.Drawing.Drawing2D.Matrix
    $mat.Translate($tx, $ty)
    $glyph.Transform($mat)
    $mat.Dispose()

    # cyan -> blue -> violet -> pink spectrum, on the diagonal of the glyph
    $gb2 = $glyph.GetBounds()
    $grect = New-Object System.Drawing.RectangleF(($gb2.X - 1), ($gb2.Y - 1), ($gb2.Width + 2), ($gb2.Height + 2))
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($grect, [System.Drawing.Color]::White, [System.Drawing.Color]::White, [single]62)
    $grad.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend(5)
    $blend.Colors = [System.Drawing.Color[]]@(
        [System.Drawing.Color]::FromArgb(255, 47, 224, 200),
        [System.Drawing.Color]::FromArgb(255, 61, 158, 255),
        [System.Drawing.Color]::FromArgb(255, 124, 92, 255),
        [System.Drawing.Color]::FromArgb(255, 194, 77, 255),
        [System.Drawing.Color]::FromArgb(255, 255, 92, 158)
    )
    $blend.Positions = [single[]]@(0.0, 0.3, 0.55, 0.78, 1.0)
    $grad.InterpolationColors = $blend
    $g.FillPath($grad, $glyph)
    $grad.Dispose()
    $glyph.Dispose()

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray() # comma keeps the byte[] intact through the pipeline
}

$sizes = @(256, 128, 64, 48, 32, 24, 16)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = DrawIconPng $s }

# assemble ICO: ICONDIR + entries + PNG payloads
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $w.Write([byte[]]$pngs[$s]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
$w.Dispose(); $out.Dispose()

# also drop a 128px preview png so the result can be eyeballed
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon-preview.png'), $pngs[128])
Write-Output ("app.ico written: " + (Get-Item (Join-Path $PSScriptRoot 'app.ico')).Length + " bytes")
