$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$dir=$PSScriptRoot
$stream=[System.IO.File]::OpenRead((Join-Path $dir 'plot.json.gz'))
$zip=New-Object System.IO.Compression.GZipStream $stream,([System.IO.Compression.CompressionMode]::Decompress)
$reader=New-Object System.IO.StreamReader $zip,([System.Text.Encoding]::UTF8)
$data=$reader.ReadToEnd() | ConvertFrom-Json
$reader.Dispose();$zip.Dispose();$stream.Dispose()
$report=Get-Content -Raw -Encoding UTF8 (Join-Path $dir 'verification.json') | ConvertFrom-Json
$font=New-Object System.Drawing.Font 'Arial',13
$small=New-Object System.Drawing.Font 'Arial',11
$titleFont=New-Object System.Drawing.Font 'Arial',20,([System.Drawing.FontStyle]::Bold)
$black=[System.Drawing.Brushes]::Black
function Canvas($w,$h) {
    $script:bmp=New-Object System.Drawing.Bitmap $w,$h
    $script:g=[System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::White)
}
function Save($name) {$bmp.Save((Join-Path $dir $name),[System.Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()}
function Pen($color,$width=1) {New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($color)),([single]$width)}
function Brush($color,$alpha=255) {New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($alpha,[System.Drawing.ColorTranslator]::FromHtml($color)))}
function SlimCoord($j) {
    if($j -le 4){$a=$j*[Math]::PI/8;return @((.1*[Math]::Sin($a)),(.1*(1-[Math]::Cos($a))),([Math]::Sin($a)),(-[Math]::Cos($a)))}
    if($j -le 16){return @(.1,(.1+5.9*($j-4)/12),1,0)}
    $a=($j-16)*[Math]::PI/32
    return @((.1*[Math]::Cos($a)),(6+.1*[Math]::Sin($a)),([Math]::Cos($a)),([Math]::Sin($a)))
}
Canvas 1580 1100
$scale=2.55
function Pt($v) {New-Object System.Drawing.PointF ([single](70+($v[0]-200)*$scale)),([single](80+(865-$v[1])*$scale))}
function Poly($coords,$brush,$pen) {
    if($coords.Count -lt 2){return}
    [System.Drawing.PointF[]]$arr=@($coords | ForEach-Object {Pt $_})
    if($arr.Count -ge 3 -and $null -ne $brush){$g.FillPolygon($brush,$arr)}
    if($null -ne $pen -and $arr.Count -ge 3){$g.DrawPolygon($pen,$arr)}
}
$gridPen=Pen '#e1e6eb'
foreach($x in 200..760 | Where-Object {$_ % 50 -eq 0}){
    $a=Pt @($x,865);$b=Pt @($x,530);$g.DrawLine($gridPen,$a,$b);$g.DrawString("X=$x",$small,$black,($a.X-22),($b.Y+8))
}
foreach($z in 530..865 | Where-Object {$_ % 50 -eq 0}){
    $a=Pt @(200,$z);$b=Pt @(760,$z);$g.DrawLine($gridPen,$a,$b);$g.DrawString("$z",$small,$black,20,($a.Y-10))
}
$g.SetClip((New-Object System.Drawing.RectangleF 70,80,1428,854))
$roof=Brush '#969ba5' 45;$wall=Brush '#9678b4' 80;$wallPen=Pen '#9c8caa'
foreach($face in $data.faces | Where-Object kind -eq 1){Poly $face.xz $roof $null}
foreach($face in $data.faces | Where-Object kind -eq 0){Poly $face.xz $wall $wallPen}
$tree=Brush '#289146' 75;$treePen=Pen '#6d9d75'
foreach($poly in $data.trees){Poly $poly $tree $treePen}
$old=Brush '#73787d' 80;$new=Brush '#235fa0'
foreach($poly in $data.old){Poly $poly $old $null}
$g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::None
foreach($poly in $data.new){Poly $poly $new $null}
$g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$moved=Pen '#d67b00' 3
foreach($poly in $data.moved_hulls){Poly $poly $null $moved}
$slim=Pen '#00c9b1' 3;$blend=Pen '#ef9d26' 4
foreach($s in $data.slim_edges){$g.DrawLine($slim,(Pt $s[0]),(Pt $s[1]))}
foreach($s in $data.blend_edges){$g.DrawLine($blend,(Pt $s[0]),(Pt $s[1]))}
$g.ResetClip()
$g.DrawString('PLAN B / Wide road + local upright barriers',$titleFont,$black,70,15)
$g.DrawString('School geometry: A = 0 / new B = 0 / C = 5 retained. External props and physics remain unverified.',$font,$black,70,49)
$g.DrawString('North wing: +5 m Z',$font,(Brush '#ab6100'),330,95)
$g.DrawString('North / start: 20 m',$font,$black,510,285)
$g.DrawString('Home: >=18 m',$font,$black,860,470)
$g.DrawString('21 m gate: 0.892 m solid clearance per side',$font,$black,850,315)
$mark=Pt @(495.0568,751.9);$g.DrawEllipse((Pen '#222222' 2),($mark.X-9),($mark.Y-9),18,18)
$g.DrawLine((Pen '#222222' 1),$mark,(New-Object System.Drawing.PointF 850,340))
$g.DrawString('School underpass / C roofs retained',$font,$black,850,580)
$g.DrawString('Blue: proposed road   Gray: original road / upper roofs   Purple: walls at world Y=3..9 m   Green: tree envelopes',$font,$black,70,985)
$g.DrawString('Cyan: slim barrier core   Gold: 30 m profile blends and moved north wing   No Unity render or game changes',$font,$black,70,1012)
$g.DrawString('Plan view in world XZ metres. Tube continues south beyond this campus-detail frame.',$font,$black,70,1040)
Save 'after.png'

Canvas 1440 900
$slice=Get-Content -Raw -Encoding UTF8 (Join-Path $dir 'gate-slice.json') | ConvertFrom-Json
$gate=$report.gate[0];$cx=$gate.center_xyz_m[0];$roadY=$gate.center_xyz_m[1];$width=$gate.road_width_m
function GPoint($x,$y){New-Object System.Drawing.PointF ([single](100+($x-475)*30)),([single](790-$y*30))}
$g.DrawString('Gate section / world Z = 751.9 m',$titleFont,$black,70,20)
$g.DrawString('Actual generated road width 18.816 m; inner wall 6.1 m; solid wall 6.2 m high.',$font,$black,70,58)
for($x=475;$x -le 515;$x+=5){$a=GPoint $x 0;$b=GPoint $x 22;$g.DrawLine($gridPen,$a,$b);$g.DrawString("$x",$small,$black,($a.X-15),800)}
for($y=0;$y -le 22;$y+=2){$a=GPoint 475 $y;$b=GPoint 515 $y;$g.DrawLine($gridPen,$a,$b);$g.DrawString("Y=$y",$small,$black,32,($a.Y-8))}
$g.SetClip((New-Object System.Drawing.RectangleF 100,120,1200,670))
$ap=GPoint ($cx-$width/2-.5) ($roadY+6)
$g.FillRectangle((Brush '#70b8e0' 45),$ap.X,$ap.Y,([single](($width+1)*30)),180)
$cp=GPoint ($cx-$width/2-.5) ($roadY+15)
$g.FillRectangle((Brush '#d4bd81' 45),$cp.X,$cp.Y,([single](($width+1)*30)),270)
$scenePen=Pen '#5b4f67' 2
foreach($line in $slice.lines){$pp=$line.points;for($j=1;$j -lt $pp.Count;$j++){$g.DrawLine($scenePen,(GPoint $pp[$j-1][0] $pp[$j-1][1]),(GPoint $pp[$j][0] $pp[$j][1]))}}
$roadPen=Pen '#235fa0' 6;$g.DrawLine($roadPen,(GPoint ($cx-$width/2) $roadY),(GPoint ($cx+$width/2) $roadY))
$wallPen2=Pen '#009e89' 5
foreach($sign in @(-1,1)){
    $edge=$cx+$sign*$width/2
    $front=New-Object System.Collections.Generic.List[System.Drawing.PointF]
    $back=New-Object System.Collections.Generic.List[System.Drawing.PointF]
    for($j=0;$j -le 32;$j++){
        $s=SlimCoord $j
        $front.Add((GPoint ($edge+$sign*$s[0]) ($roadY+$s[1])))
        $back.Add((GPoint ($edge+$sign*($s[0]+.1*$s[2])) ($roadY+$s[1]+.1*$s[3])))
    }
    $g.DrawLines($wallPen2,$front.ToArray());$g.DrawLines((Pen '#009e89' 2),$back.ToArray())
}
$g.ResetClip()
$g.DrawString('A: light blue (road + 0.5 m, h=0..6 m)   C: beige (h=6..15 m, retained)',$font,$black,70,838)
$g.DrawString('Solid envelope = 19.216 m within 21 m opening; west / east gap = 0.892 m each. School slice from full FBX faces.',$font,$black,70,866)
Save 'section-gate.png'

Canvas 1440 900
function SPoint($x,$h){New-Object System.Drawing.PointF ([single](105+$x*78)),([single](780-$h*45))}
$g.DrawString('Cross-section comparison / metres from road edge',$titleFont,$black,70,20)
$g.DrawString('DefaultArc is retained away from buildings. SlimUpright is a separate profile; height is preserved.',$font,$black,70,58)
for($x=0;$x -le 13;$x++){$g.DrawLine($gridPen,(SPoint $x 0),(SPoint $x 14));$pt=SPoint $x 0;$g.DrawString("$x",$small,$black,($pt.X-5),790)}
for($h=0;$h -le 14;$h+=2){$g.DrawLine($gridPen,(SPoint 0 $h),(SPoint 13 $h));$pt=SPoint 0 $h;$g.DrawString("h=$h",$small,$black,45,($pt.Y-8))}
$default=New-Object System.Collections.Generic.List[System.Drawing.PointF]
for($j=0;$j -le 16;$j++){$a=$j*[Math]::PI/48;$default.Add((SPoint (12*[Math]::Sin($a)) (12*(1-[Math]::Cos($a)))))}
for($j=1;$j -le 16;$j++){$a=[Math]::PI/3+$j*(165*[Math]::PI/180-[Math]::PI/3)/16;$default.Add((SPoint (12*[Math]::Sin([Math]::PI/3)+4.5*([Math]::Sin($a)-[Math]::Sin([Math]::PI/3))) (6+4.5*(.5-[Math]::Cos($a)))))}
$g.DrawLines((Pen '#6b7f9f' 4),$default.ToArray())
$thin=New-Object System.Collections.Generic.List[System.Drawing.PointF]
for($j=0;$j -le 32;$j++){$s=SlimCoord $j;$thin.Add((SPoint $s[0] $s[1]))}
$g.DrawLines((Pen '#009e89' 5),$thin.ToArray())
$g.DrawString('Slim: inner reach <=0.10 m; solid reach <=0.20 m',$font,(Brush '#008575'),170,450)
$g.DrawString('6.10 m inner / 6.20 m solid height',$font,(Brush '#008575'),170,478)
$g.DrawString('Default: radius 12 m',$font,(Brush '#596f91'),780,150)
$g.DrawString('At h=3 m: reach 7.936 m (16 facets)',$font,(Brush '#596f91'),610,650)
$g.DrawString('At h=6 m: reach 10.392 m',$font,(Brush '#596f91'),800,505)
$g.DrawString('Slim detail (schematic)',$font,$black,1050,180)
$g.DrawRectangle((Pen '#adb6bd'),1040,215,300,420)
$g.DrawLine((Pen '#009e89' 4),1100,600,1100,270)
$g.DrawLine((Pen '#009e89' 2),1160,600,1160,270)
$g.DrawString('Inner   0.1 m   back',$small,$black,1070,615)
$g.DrawString('Panel, collider and all rails use the same profile. The inset exaggerates width for visibility.',$font,$black,70,844)
$g.DrawString('A 6 m wall is a geometry requirement; vehicle containment remains a future Unity physics check.',$font,$black,70,873)
Save 'section-profiles.png'

Canvas 1440 760
$g.DrawString('30 m transition / DefaultArc to SlimUpright',$titleFont,$black,70,20)
$g.DrawString('Corresponding 33 vertices blend with smoothstep; both ends have zero first derivative.',$font,$black,70,58)
function TPoint($x,$y){New-Object System.Drawing.PointF ([single](110+$x*36)),([single](600-$y*35))}
for($s=0;$s -le 30;$s+=5){$pt=TPoint $s 0;$g.DrawLine($gridPen,$pt,(TPoint $s 14));$g.DrawString("$s m",$small,$black,($pt.X-10),615)}
for($h=0;$h -le 14;$h+=2){$pt=TPoint 0 $h;$g.DrawLine($gridPen,$pt,(TPoint 30 $h));$g.DrawString("$h",$small,$black,70,($pt.Y-8))}
$heights=New-Object System.Collections.Generic.List[System.Drawing.PointF]
$reach=New-Object System.Collections.Generic.List[System.Drawing.PointF]
for($j=0;$j -le 120;$j++){$t=$j/120;$a=$t*$t*(3-2*$t);$heights.Add((TPoint ($t*30) ((1-$a)*12.596666+$a*6.1)));$reach.Add((TPoint ($t*30) ((1-$a)*11.01+$a*.1)))}
$g.DrawLines((Pen '#235fa0' 4),$heights.ToArray());$g.DrawLines((Pen '#009e89' 4),$reach.ToArray())
$g.DrawString('Blue: maximum inner height (m)',$font,(Brush '#235fa0'),390,190)
$g.DrawString('Green: conservative maximum inner reach (m)',$font,(Brush '#008575'),360,520)
$g.DrawString('Raw progress 1690 -> 1720 m. Reverse transition: 210 -> 240 m. Closed seam stays Slim.',$font,$black,70,675)
$g.DrawString('Maximum reach-change bound: 0.546 m/m. This is separate from the road-width rate limit 0.2 m/m.',$font,$black,70,708)
Save 'section-transition.png'
