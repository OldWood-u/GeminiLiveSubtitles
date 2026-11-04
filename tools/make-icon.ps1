# Generates src/GeminiLiveSubtitles/Assets/app.ico (PNG-compressed multi-size ICO).
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\src\GeminiLiveSubtitles\Assets\app.ico"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
$sizes = 16, 24, 32, 48, 64, 256
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
  $g.Clear([System.Drawing.Color]::Transparent)
  $r = [Math]::Max(2, [int]($s * 0.22))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2; $w = $s - 1
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
  $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $s, $s), [System.Drawing.Color]::FromArgb(255, 66, 133, 244), [System.Drawing.Color]::FromArgb(255, 155, 81, 224))
  $g.FillPath($brush, $path)
  # subtitle bars
  $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
  $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 255, 255, 255))
  $bh = [Math]::Max(2, [int]($s * 0.11))
  $g.FillRectangle($soft, [int]($s * 0.28), [int]($s * 0.50), [int]($s * 0.44), $bh)
  $g.FillRectangle($white, [int]($s * 0.18), [int]($s * 0.68), [int]($s * 0.64), $bh)
  # spark (Gemini-like 4-point star)
  $c = $s * 0.5; $cy = $s * 0.28; $k = $s * 0.16
  $pts = @(
    (New-Object System.Drawing.PointF $c, ($cy - $k)), (New-Object System.Drawing.PointF ($c + $k * 0.25), ($cy - $k * 0.25)),
    (New-Object System.Drawing.PointF ($c + $k), $cy), (New-Object System.Drawing.PointF ($c + $k * 0.25), ($cy + $k * 0.25)),
    (New-Object System.Drawing.PointF $c, ($cy + $k)), (New-Object System.Drawing.PointF ($c - $k * 0.25), ($cy + $k * 0.25)),
    (New-Object System.Drawing.PointF ($c - $k), $cy), (New-Object System.Drawing.PointF ($c - $k * 0.25), ($cy - $k * 0.25)))
  $g.FillPolygon($white, [System.Drawing.PointF[]]$pts)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , $ms.ToArray()
  $bmp.Dispose()
}
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Wrote $out"
