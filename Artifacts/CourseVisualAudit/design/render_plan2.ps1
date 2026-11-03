$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = $PSScriptRoot
$stream = [System.IO.File]::OpenRead((Join-Path $dir 'plot-plan2.json.gz'))
$zip = New-Object System.IO.Compression.GZipStream $stream,([System.IO.Compression.CompressionMode]::Decompress)
$reader = New-Object System.IO.StreamReader $zip,([System.Text.Encoding]::UTF8)
$data = $reader.ReadToEnd() | ConvertFrom-Json
$reader.Dispose();$zip.Dispose();$stream.Dispose()
$bmp = New-Object System.Drawing.Bitmap 1580,1060
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font 'Arial',13
$titleFont = New-Object System.Drawing.Font 'Arial',19,([System.Drawing.FontStyle]::Bold)
$black = [System.Drawing.Brushes]::Black
$scale = 2.55
function Pt($v) { New-Object System.Drawing.PointF ([single](70+($v[0]-200)*$scale)),([single](80+(865-$v[1])*$scale)) }
function Poly($coords,$brush,$pen) {
    if ($coords.Count -lt 2) { return }
    [System.Drawing.PointF[]]$arr = @($coords | ForEach-Object { Pt $_ })
    if ($arr.Count -ge 3 -and $null -ne $brush) { $g.FillPolygon($brush,$arr) }
    if ($null -ne $pen) { $g.DrawPolygon($pen,$arr) }
}
$gridPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(225,230,235)),1
foreach ($x in 200..760 | Where-Object { $_ % 50 -eq 0 }) {
    $a=Pt @($x,865);$b=Pt @($x,530);$g.DrawLine($gridPen,$a,$b)
    $g.DrawString("X=$x",$font,$black,($a.X-22),($b.Y+8))
}
foreach ($z in 530..865 | Where-Object { $_ % 50 -eq 0 }) {
    $a=Pt @(200,$z);$b=Pt @(760,$z);$g.DrawLine($gridPen,$a,$b)
    $g.DrawString("$z",$font,$black,20,($a.Y-10))
}
$roofBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45,150,155,165))
$lowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(80,150,120,180))
$wallPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(130,110,90,145)),1
$g.SetClip((New-Object System.Drawing.RectangleF 70,80,1428,854))
foreach ($face in $data.faces | Where-Object kind -eq 1) { Poly $face.xz $roofBrush $null }
foreach ($face in $data.faces | Where-Object kind -eq 0) { Poly $face.xz $lowBrush $wallPen }
$treeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(75,40,145,70))
$treePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110,35,125,55)),1
foreach ($poly in $data.trees) { Poly $poly $treeBrush $treePen }
$oldBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(80,115,120,125))
foreach ($poly in $data.old) { Poly $poly $oldBrush $null }
$newBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,35,95,160))
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
foreach ($poly in $data.new) { Poly $poly $newBrush $null }
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$movedPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::DarkOrange),3
foreach ($poly in $data.moved_hulls) { Poly $poly $null $movedPen }
$failPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Crimson),5
foreach ($seg in $data.failed_segments) { $g.DrawLine($failPen,(Pt $seg[0]),(Pt $seg[1])) }
$g.ResetClip()
$g.DrawString('PLAN 2 / Bounded placement candidate',$titleFont,$black,70,15)
$g.DrawString($data.legend,$font,[System.Drawing.Brushes]::Crimson,70,48)
$g.DrawString('North wing: +10 m Z, same Y',$font,[System.Drawing.Brushes]::DarkOrange,395,110)
$a=Pt @(360,801);$b=Pt @(360,813);$g.DrawLine($movedPen,$a,$b)
$g.DrawString('Gate: 21 m opening',$font,$black,850,330)
$g.DrawLine($failPen,(Pt @(494,752)),(Pt @(520,765)))
$g.DrawString('Required width at 3 m: 27.875 m',$font,$black,850,352)
$g.DrawString('C roofs retained above the road',$font,$black,850,550)
$g.DrawString('Blue: proposed road    Gray: existing road / upper roof envelopes    Purple: walls at world Y=3..9 m',$font,$black,70,980)
$g.DrawString('Orange: moved architectural unit    Red: original B(0..3 m) residual segments    Green: tree XZ envelopes',$font,$black,70,1006)
$g.DrawString('Campus detail; tube continues south beyond frame. Scenery outlines are conservative projections. Unity not run.',$font,$black,70,1032)
$bmp.Save((Join-Path $dir 'after-plan2.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()
