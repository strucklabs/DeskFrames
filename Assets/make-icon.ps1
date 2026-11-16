# Erzeugt Assets\DeskFrames.ico: dunkler Bilderrahmen mit Landschaft (Himmel, Sonne, Berge).
# Mehrere Größen (16–256) als PNG im ICO-Container; scharf auch bei 150 % Skalierung.
# Aufruf: powershell -NoProfile -File Assets\make-icon.ps1
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    # Rahmen
    $frame = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x2B, 0x2B, 0x2E))
    $g.FillRectangle($frame, [single](2 * $s), [single](4 * $s), [single](28 * $s), [single](24 * $s))
    # Himmel
    $inner = New-Object System.Drawing.RectangleF ([single](5 * $s)), ([single](7 * $s)), ([single](22 * $s)), ([single](18 * $s))
    $sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush $inner, ([System.Drawing.Color]::FromArgb(0x7C, 0xC4, 0xF0)), ([System.Drawing.Color]::FromArgb(0xD8, 0xEE, 0xFA)), 90.0
    $g.FillRectangle($sky, $inner)
    # Sonne
    $sun = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xF6, 0xC3, 0x45))
    $g.FillEllipse($sun, [single](19 * $s), [single](9 * $s), [single](5 * $s), [single](5 * $s))
    # Berge
    $hill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x3F, 0x8F, 0x5A))
    $pts = @(
        (New-Object System.Drawing.PointF ([single](5 * $s)), ([single](25 * $s))),
        (New-Object System.Drawing.PointF ([single](13 * $s)), ([single](14 * $s))),
        (New-Object System.Drawing.PointF ([single](19 * $s)), ([single](21 * $s))),
        (New-Object System.Drawing.PointF ([single](22 * $s)), ([single](18 * $s))),
        (New-Object System.Drawing.PointF ([single](27 * $s)), ([single](25 * $s))))
    $g.FillPolygon($hill, [System.Drawing.PointF[]]$pts)

    $g.Dispose(); $frame.Dispose(); $sky.Dispose(); $sun.Dispose(); $hill.Dispose()
    return $bmp
}

# Klassische Bitmap (32 Bit mit Alpha) für kleine Größen – lesbar für alle Windows-/NET-Versionen
function To-Dib([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER; Höhe doppelt (Farbe + Maske)
    $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32]($n * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $n - 1; $y -ge 0; $y--) {          # von unten nach oben
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    # UND-Maske: alles 0 (Transparenz steckt im Alpha-Kanal), Zeilen auf 4 Byte aufgefüllt
    $maskRow = [int]([math]::Ceiling($n / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $n)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = To-Dib $bmp
    }
    $bmp.Dispose()
    , $data
}

# ICO-Container: Kopf (6 Byte), je Bild ein Verzeichniseintrag (16 Byte), dann die PNG-Daten
$out = Join-Path $PSScriptRoot "DeskFrames.ico"
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
"Written $out ($((Get-Item $out).Length) bytes, sizes $($sizes -join ', '))"
