from pathlib import Path
import json,math,shutil
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A3,landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor
ROOT=Path(__file__).resolve().parent
CFG=json.load(open(ROOT/('mount-parameters.json' if (ROOT/'mount-parameters.json').exists() else 'dimensions.json')))
SITE=ROOT.parents[1]
OUT=ROOT/'output/pdf' if (ROOT/'mount-parameters.json').exists() else SITE/'output/pdf'
OUT.mkdir(parents=True,exist_ok=True)
PDF=OUT/'macbook-recessed-mount.pdf';W,H=landscape(A3)
pdfmetrics.registerFont(TTFont('JP','/System/Library/Fonts/Supplemental/Arial Unicode.ttf'))
c=canvas.Canvas(str(PDF),pagesize=(W,H),pageCompression=1)
c.setTitle('CabinetMacMount / MacBook Pro 13-inch 2020 / 2150 mm筐体')
INK='#182530';MUTED='#5b6c79';BLUE='#009be8';PINK='#ea008c';WOOD='#c99858';SILVER='#b9c3ca'
def text(x,y,t,size=11,color=INK):c.setFillColor(HexColor(color));c.setFont('JP',size);c.drawString(x,y,str(t))
def line(x1,y1,x2,y2,color=INK,width=1,dash=None):
    c.setStrokeColor(HexColor(color));c.setLineWidth(width);c.setDash(dash or []);c.line(x1,y1,x2,y2);c.setDash()
def box(x,y,w,h,color,stroke=None):
    c.setFillColor(HexColor(color));c.setStrokeColor(HexColor(stroke or color));c.rect(x,y,w,h,fill=1,stroke=bool(stroke))
def poly(ps,color,stroke=INK):
    p=c.beginPath();p.moveTo(*ps[0])
    for v in ps[1:]:p.lineTo(*v)
    p.close();c.setFillColor(HexColor(color));c.setStrokeColor(HexColor(stroke));c.drawPath(p,fill=1,stroke=1)
def wrap(x,y,t,width=490,size=11,leading=19):
    row=''
    for ch in t:
        if ch=='\n' or pdfmetrics.stringWidth(row+ch,'JP',size)>width:
            text(x,y,row,size);row='';y-=leading
        if ch!='\n':row+=ch
    if row:text(x,y,row,size);y-=leading
    return y
def title(n,t,sub):
    box(0,H-10,W/2,10,BLUE);box(W/2,H-10,W/2,10,PINK)
    text(40,H-54,t,23);text(40,H-77,sub,11,MUTED)
    line(40,H-94,W-40,H-94,'#dce3e8')
    text(40,23,'2026-10-19 / 全高2150 mmで校正 / 寸法mm / 構造・接合・端子の逃げは実機確認後に確定',9,MUTED)
    c.setFont('JP',10);c.drawRightString(W-40,23,f'{n} / 3')
def section(x,y,t):text(x,y,t,15);line(x,y-11,x+490,y-11,'#dce3e8',1.5)
def hdim(x1,x2,y,label):
    line(x1,y,x2,y,MUTED,.7)
    for x in [x1,x2]:line(x,y-4,x,y+4,MUTED,.7)
    c.setFont('JP',10);c.setFillColor(HexColor(MUTED));c.drawCentredString((x1+x2)/2,y-16,label)
def vdim(x,y1,y2,label):
    line(x,y1,x,y2,MUTED,.7)
    for y in [y1,y2]:line(x-4,y,x+4,y,MUTED,.7)
    c.saveState();c.translate(x-9,(y1+y2)/2);c.rotate(90);c.setFont('JP',10);c.setFillColor(HexColor(MUTED));c.drawCentredString(0,0,label);c.restoreState()
def rows(x,y,rs,width=500):
    for key,value in rs:
        text(x,y,key,10,MUTED);text(x+220,y,value,11);line(x,y-11,x+width,y-11,'#e5e9ed',.6);y-=32
    return y
d=CFG['device'];window=CFG['window'];cavity=CFG['cavity'];cover=CFG['cover']
hinge=(-135,d['hinge_floor_mm']);L=d['hinge_to_lid_top_model_mm']
up=(math.sin(math.radians(20)),math.cos(math.radians(20)));down=(.5,-math.sqrt(3)/2)
lidtop=(hinge[0]+up[0]*L,hinge[1]+up[1]*L)
front=(hinge[0]+down[0]*d['depth_mm'],hinge[1]+down[1]*d['depth_mm'])
assert abs(lidtop[1]-1630)<.01 and abs(math.degrees(math.acos(up[0]*down[0]+up[1]*down[1]))-130)<.001
title(1,'MacBook Pro 13インチ2020 / 新しい窓位置','筐体全高2150 mm。観戦窓の開口上端を、天端から500 mmの高さへ合わせる。')
section(40,710,'A  側面位置と開口寸法')
s=.245;ox=145;oy=120
outline=[(-110.1,215),(17.5,215),(17.5,170),(0,152.5),(0,73.5),(-10.6,73.5),(-10.6,28),(0,28),(0,0),(-92.6,0),(-92.6,28),(-82,28),(-82,73.5),(-92.6,73.5),(-92.6,152.5),(-110.1,170)]
poly([(ox+(u+110.1)*10*s,oy+z*10*s) for u,z in outline],INK)
wx=ox+(1276-360)/2*s;wy=oy+1410*s
box(wx,wy,360*s,240*s,'#ffffff')
fw=window['outline_width_mm'];gap=window['black_clearance_mm']
# Paint extends outwards from the clear opening; keep the opening itself unchanged.
box(wx-fw*s,wy-fw*s,fw*s,(240+2*fw)*s,PINK)
box(wx+360*s,wy-fw*s,fw*s,(240+2*fw)*s,BLUE)
for zz in [wy-fw*s,wy+240*s]:
    box(wx,zz,180*s,fw*s,PINK);box(wx+180*s,zz,180*s,fw*s,BLUE)
vdim(100,oy,oy+2150*s,'筐体全高 2150')
vdim(ox+1276*s+35,wy+240*s,oy+2150*s,'天端から 500')
vdim(ox+1276*s+66,oy,wy+240*s,'床から開口上端 1650')
hdim(wx,wx+360*s,wy-24,'開口幅 360')
text(ox,80,'開口高さ240 / 下端は床から1410',11)
text(ox,61,'枠20 mm＋外側黒余白35 mm。穴の上端を1650に合わせる。',9,MUTED)
section(640,710,'B  130°開いた配置・側断面')
ss=.86;xx=940;yy=190
P=lambda p:(xx+p[0]*ss,yy+(p[1]-1210)*ss)
box(*P((-170,1210)),170*ss,450*ss,'#edf1f4')
line(*P((0,1190)),*P((0,1690)),INK,1.5)
box(*P((0,1200)),12*ss,210*ss,INK);box(*P((0,1650)),12*ss,30*ss,INK)
normal=(up[1],-up[0]);bn=(-down[1],down[0])
lid=[hinge,lidtop,(lidtop[0]-normal[0]*3.6,lidtop[1]-normal[1]*3.6),(hinge[0]-normal[0]*3.6,hinge[1]-normal[1]*3.6)]
body=[hinge,front,(front[0]-bn[0]*12,front[1]-bn[1]*12),(hinge[0]-bn[0]*12,hinge[1]-bn[1]*12)]
poly([P(p) for p in lid],SILVER);poly([P(p) for p in body],SILVER)
line(*P(hinge),*P(lidtop),BLUE,2)
hx,hy=P(hinge);c.setStrokeColor(HexColor('#b75b19'));c.arc(hx-35,hy-35,hx+35,hy+35,-60,130);text(hx+40,hy+2,'130°',12,'#b75b19')
hdim(*[P((a,1210))[0] for a in [-170,0]],P((0,1210))[1]-25,'内空奥行 170')
text(787,yy+450*ss+25,'内側',11);text(962,yy+450*ss+25,'観客側 →',11)
text(955,590,'開口上端1650',10);line(950,585,*P((0,1650)),MUTED,.6)
text(955,540,'画面上端モデル1630',10);line(950,535,*P(lidtop),MUTED,.6)
text(955,255,'キーボードを下へ60°',10);line(950,250,*P(front),MUTED,.6)
rows(640,139,[('Mac公式外形（閉じた状態）','304.1 × 212.4 × 15.6'),('Macのヒンジ高さ（設計配置）',f"床から {hinge[1]:.1f}"),('外への突出','表板12程度 + 金物')],480)
c.showPage()
title(2,'支持枠・着脱台の構造','画面やヒンジを吊らず、本体底面の受けと側縁の固定具で支える。')
section(40,710,'C  新設枠と着脱カバー')
sx=.6;fx=200;fy=230
box(fx-40*sx,fy-70*sx,40*sx,590*sx,WOOD,INK);box(fx+400*sx,fy-70*sx,40*sx,590*sx,WOOD,INK)
box(fx,fy+450*sx,400*sx,70*sx,WOOD,INK);box(fx,fy-70*sx,400*sx,70*sx,WOOD,INK)
c.setStrokeColor(HexColor(MUTED));c.setDash(4,3);c.rect(fx-40*sx,fy-10*sx,480*sx,480*sx,stroke=1,fill=0);c.setDash()
hdim(fx,fx+400*sx,fy-70,'内空幅400')
vdim(fx+400*sx+32,fy,fy+450*sx,'内空高さ450')
text(65,626,'支柱40×45：既存上下枠に接合',11);text(65,602,'上の横桟：床1660〜1730',11);text(65,580,'下の受け：上端を床1210',11)
wrap(40,125,'破線は着脱表板480×480×厚12。開口は360×240。既存縦材2本は開口と重なるため、新支柱へ置き換える構造案。',510)
section(640,710,'D  着脱台・固定・余白')
ys=653
for t in ['左右の側枠＋細い底面受け2本で本体を支持。','本体幅304.1に対し内空400。左右の余白は各47.95。','端子の位置を避けて側枠を切り欠く。USB-Cケーブルの曲げも確認。','底面の通気を塞がず、接触部には薄いゴム当て。','本体縁の小さな受けで滑り止め。画面・キー・トラックパッドを締めない。','受け台は左右の金物へ固定。表板と台は別々に取り外せる。','取り外し：表板 → 配線 → 台固定ボルト → Macを載せた台。','新しい支柱を上下枠へ接合してから、既存縦材を順番に置き換える。']:
    ys=wrap(640,ys,'・'+t,495,11,19)-18
text(640,180,'実機に合わせる取付位置',13,'#b75b19')
wrap(640,154,'公式15.6は閉じた全厚。3Dの本体12＋画面3.6、ヒンジ位置・ベゼル位置は表示用の近似です。受け位置と端子の逃げは実機で合わせます。',495,10,18)
c.showPage()
title(3,'寸法の再確認と確定範囲','全高2150 mmから1モデル単位=10 mmを確定。新しい寸法を3D・塗装面PDFと共有する。')
section(40,710,'E  寸法一覧')
rows(40,661,[('筐体全高（ユーザー指定）','2150'),('筐体本体幅（モデル換算）','1206'),('全長・椅子を含む（モデル換算）','2652.8'),('観戦窓の開口上端','床1650 / 天端から500'),('観戦窓の開口下端','床1410'),('観戦窓の開口幅×高さ','360 × 240'),('塗装の枠幅 / 外側の黒余白','20 / 35'),('着脱表板','480 × 480 × 厚12'),('収納内空 幅×高さ×奥行','400 × 450 × 170'),('Mac公式外形 幅×奥行×全厚','304.1 × 212.4 × 15.6'),('開き / 画面の外傾 / 本体の下傾','130° / 20° / 60°')])
section(640,710,'F  公式仕様と設計値の区別')
wrap(640,660,'Appleの2020年13インチMacBook Proは、Intelの2ポート・4ポート版とM1版で外形寸法が共通です。幅304.1、奥行212.4、閉じた高さ15.6 mmを採用しました。',495,11,20)
wrap(640,560,'13.3インチ・2560×1600から計算する画面有効域は約286.5×179.0 mm。これは対角値と縦横比からの概算で、ベゼルやヒンジを含む採寸値ではありません。',495,11,20)
wrap(640,460,'窓の360×240、内空400×450×170は、このMacを置くための設計寸法です。全高以外の筐体寸法は提供モデルの比例換算で、製作物の個別実測ではありません。',495,11,20)
wrap(640,355,'ヒンジから画面外周上端までを212.4とする表示モデルで、上端を床1630に設定し、ヒンジ高さを1430.4に計算しています。実機のヒンジ軸位置を測った確定値ではありません。',495,11,20)
text(640,235,'Apple公式資料',12)
for i,src in enumerate(CFG['sources']):
    yy=210-i*38;text(640,yy,src['title'],9);text(640,yy-15,src['url'],9,MUTED);c.linkURL(src['url'],(640,yy-20,1130,yy+10),relative=0)
c.save()
print(PDF)
