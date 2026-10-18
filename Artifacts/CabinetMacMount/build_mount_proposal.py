from pathlib import Path
import json, math
from reportlab.pdfgen import canvas
from reportlab.lib.colors import HexColor, Color, white
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.pagesizes import A3, landscape

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'output'/'pdf'
OUT.mkdir(parents=True,exist_ok=True)
pdfmetrics.registerFont(TTFont('JP','/System/Library/Fonts/Supplemental/Arial Unicode.ttf'))
PW,PH=landscape(A3)
C=canvas.Canvas(str(OUT/'macbook-recessed-mount.pdf'),pagesize=(PW,PH))
C.setTitle('レースゲーム筐体 / MacBook埋込観戦窓 構造案')
C.setAuthor('Cabinet design study')
INK=HexColor('#182530'); MUTED=HexColor('#586774'); LIGHT=HexColor('#edf1f4')
BLUE=HexColor('#168bdf'); PINK=HexColor('#eb238b'); WOOD=HexColor('#c99858')
SILVER=HexColor('#bec7ce'); ORANGE=HexColor('#b75b19'); GREEN=HexColor('#25816f')

def text(x,y,s,size=11,color=INK,align='left'):
    C.setFillColor(color); C.setFont('JP',size)
    {'left':C.drawString,'center':C.drawCentredString,'right':C.drawRightString}[align](x,y,str(s))
def line(x1,y1,x2,y2,color=INK,lw=1,dash=None):
    C.setStrokeColor(color); C.setLineWidth(lw); C.setDash(dash or [])
    C.line(x1,y1,x2,y2); C.setDash([])
def rect(x,y,w,h,fill=None,stroke=INK,lw=1):
    C.setLineWidth(lw)
    if fill:C.setFillColor(fill)
    if stroke:C.setStrokeColor(stroke)
    C.rect(x,y,w,h,fill=bool(fill),stroke=bool(stroke))
def poly(pts,fill=None,stroke=INK,lw=1):
    p=C.beginPath();p.moveTo(*pts[0])
    for q in pts[1:]:p.lineTo(*q)
    p.close();C.setLineWidth(lw)
    if fill:C.setFillColor(fill)
    if stroke:C.setStrokeColor(stroke)
    C.drawPath(p,fill=bool(fill),stroke=bool(stroke))
def arrow(x1,y1,x2,y2,color=INK,lw=1):
    line(x1,y1,x2,y2,color,lw)
    a=math.atan2(y2-y1,x2-x1)
    for s in [-1,1]:
        t=a+s*.45;line(x2,y2,x2-7*math.cos(t),y2-7*math.sin(t),color,lw)
def dim(x1,y1,x2,y2,label,offset=0):
    if abs(y1-y2)<.1:
        yy=y1+offset
        for xx in [x1,x2]:line(xx,y1,xx,yy+4,MUTED,.55)
        line(x1,yy,x2,yy,MUTED,.6)
        for xx in [x1,x2]:line(xx-3,yy-4,xx+3,yy+4,MUTED,.7)
        text((x1+x2)/2,yy+5,label,10,MUTED,'center')
    else:
        xx=x1+offset
        for yy in [y1,y2]:line(x1,yy,xx+4,yy,MUTED,.55)
        line(xx,y1,xx,y2,MUTED,.6)
        for yy in [y1,y2]:line(xx-4,yy-3,xx+4,yy+3,MUTED,.7)
        C.saveState();C.translate(xx-5,(y1+y2)/2);C.rotate(90);text(0,0,label,10,MUTED,'center');C.restoreState()
def header(num,title,sub):
    rect(0,PH-12,PW/2,12,BLUE,None);rect(PW/2,PH-12,PW/2,12,PINK,None)
    text(40,PH-55,title,24)
    text(40,PH-78,sub,11,MUTED)
    line(40,PH-94,PW-40,PH-94,LIGHT,2)
    text(40,24,'概念設計 / 寸法は提案値 mm / 実機・実寸・接合強度は未確認 / 図をそのまま切断型紙にしない',9,MUTED)
    text(PW-40,24,f'{num} / 3',10,MUTED,'right')
def title(x,y,t):text(x,y,t,15);line(x,y-9,x+480,y-9,LIGHT,2)
def notes(x,y,items,width=500,leading=22,size=11):
    import textwrap
    for item in items:
        # Explicit line breaks keep Japanese engineering labels deterministic.
        for row in item.split('\n'):
            text(x,y,row,size);y-=leading
        y-=6
    return y

# Local section: outward is +x, up is +z. Lid top leans 20 degrees outward.
hinge=(-135.,1280.)
lid_angle=70.; body_angle=-60.; opening=lid_angle-body_angle
D=250.;H=250.;T=18.;W=360.
lv=(math.cos(math.radians(lid_angle)),math.sin(math.radians(lid_angle)))
bv=(math.cos(math.radians(body_angle)),math.sin(math.radians(body_angle)))
bn=(-bv[1],bv[0])
lid_top=(hinge[0]+H*lv[0],hinge[1]+H*lv[1])
front=(hinge[0]+D*bv[0],hinge[1]+D*bv[1])
body=[hinge,front,(front[0]-T*bn[0],front[1]-T*bn[1]),(hinge[0]-T*bn[0],hinge[1]-T*bn[1])]
lid=[hinge,lid_top,(lid_top[0]-5*lv[1],lid_top[1]+5*lv[0]),(hinge[0]-5*lv[1],hinge[1]+5*lv[0])]
assert opening==130
assert max(p[0] for p in body+lid)<0
assert min(p[0] for p in body+lid)>-170
assert min(p[1] for p in body+lid)>1040
assert max(p[1] for p in body+lid)<1560
params={'status':'concept - not a fabrication or structural approval','units':'mm',
 'device_envelope':{'width':W,'base_depth':D,'base_thickness':T,'lid_length':H},
 'cavity_clear':{'width':400,'height':520,'depth':170},
 'front_cover':{'width':480,'height':560,'thickness':12,'viewing_opening':[400,270]},
 'opening_angle_deg':opening,'screen_outward_lean_deg':20,'base_downward_angle_deg':60,
 'hinge_from_side_plane_and_floor':hinge,'screen_top':lid_top,'keyboard_front':front,
 'body_and_lid_max_depth':-min(p[0] for p in body+lid),
 'sources':{'blender':'racing-cabinet-flow.blend','source_units_confirmed':False,'conditional_mm_per_model_unit':10}}
(ROOT/'mount-parameters.json').write_text(json.dumps(params,ensure_ascii=False,indent=2))

header(1,'MacBookを側板の内側に収める','画面窓＋取り外せる傾斜台。キーボード側を斜め下に逃がし、外への張り出しを表板程度に抑える。')
title(40,PH-128,'A  観客側から見た外観')
s=.66;fx=103;fy=195
rect(fx,fy,480*s,560*s,INK,INK)
# Same-direction stripes, switching color at the middle.
for a in [-190,-70,50]:
    C.saveState();p=C.beginPath();p.rect(fx,fy,480*s,560*s);C.clipPath(p,stroke=0)
    poly([(fx+a*s,fy),(fx+(a+50)*s,fy),(fx+(a+610)*s,fy+560*s),(fx+(a+560)*s,fy+560*s)],BLUE,None)
    C.restoreState()
    C.saveState();p=C.beginPath();p.rect(fx+240*s,fy,240*s,560*s);C.clipPath(p,stroke=0)
    poly([(fx+a*s,fy),(fx+(a+50)*s,fy),(fx+(a+610)*s,fy+560*s),(fx+(a+560)*s,fy+560*s)],PINK,None)
    C.restoreState()
wx=fx+40*s;wy=fy+240*s
rect(wx,wy,400*s,270*s,HexColor('#090e13'),SILVER,.8)
rect(wx+20*s,wy+17*s,360*s,236*s,HexColor('#203c4a'),None)
text(fx+240*s,wy+132*s,'LIVE RACE',19,white,'center')
text(fx+240*s,fy+150*s,'SERVICE',12,white,'center')
for xx in [fx+20*s,fx+460*s]:
    for zz in [fy+30*s,fy+280*s,fy+530*s]:
        C.setFillColor(SILVER);C.circle(xx,zz,2.7,fill=1,stroke=0)
dim(fx,fy,fx+480*s,fy,'表板 480',-33)
dim(fx,fy,fx,fy+560*s,'表板 560',-35)
dim(wx,wy+270*s,wx+400*s,wy+270*s,'視窓 400',46)
notes(57,136,['表板：12 mm合板。外側への張り出しは約12〜16 mm。',
               '下半分はキーボードを隠す着脱カバー。視窓に透明板は付けない。',
               '画面は奥まるため、斜めからの見切れを実機で確認する。'],leading=18,size=10)

title(600,PH-128,'B  側断面：130°開いた収納姿勢')
ss=.82; ox=945; oz=188
def P(p):return ox+p[0]*ss,oz+(p[1]-1040)*ss
# Interior envelope.
rect(*P((-170,1040)),170*ss,520*ss,LIGHT,None)
line(*P((-170,1030)),*P((-170,1580)),MUTED,.8,[4,3])
line(*P((0,1010)),*P((0,1595)),INK,1.6)
rect(*P((0,1020)),12*ss,240*ss,INK,None)
rect(*P((0,1530)),12*ss,50*ss,INK,None)
text(ox-170*ss,oz+520*ss+24,'筐体内部',12)
text(ox+35,oz+520*ss+24,'観客側 →',12)
poly([P(p) for p in lid],SILVER,INK)
poly([P(p) for p in body],SILVER,INK)
line(*P(hinge),*P(lid_top),BLUE,3)
# Two narrow bottom support rails, with no cover over keyboard.
normal=bn
rail=[(body[3][0]-3*normal[0],body[3][1]-3*normal[1]),
      (body[2][0]-3*normal[0],body[2][1]-3*normal[1]),
      (body[2][0]-11*normal[0],body[2][1]-11*normal[1]),
      (body[3][0]-11*normal[0],body[3][1]-11*normal[1])]
poly([P(p) for p in rail],WOOD,INK,.7)
dim(*P((-170,1030)),*P((0,1030)),'必要内空奥行 170',-28)
dim(*P((-170,1040)),*P((-170,1560)),'必要内空高さ 520',-43)
hx,hy=P(hinge);C.setStrokeColor(ORANGE);C.setLineWidth(1)
C.arc(hx-35,hy-35,hx+35,hy+35,-60,130)
text(hx+44,hy+6,'130°',12,ORANGE)
arrow(1105,550,*P(lid_top),MUTED)
text(1105,568,'画面上端：約50 mm内側',10,MUTED,'right')
arrow(1105,320,*P((-40,1120)),MUTED)
text(1105,342,'キーボード側を下へ60°',10,MUTED,'right')
text(615,128,'仮のMac外形：幅360 × 奥行250 × 厚18 / 画面側長さ250',10)
text(615,107,'本体・画面の計算上の奥行：約151。支持材・通気の余白を含め170を確保。',10)
text(615,86,'画面は上端が観客側へ20°傾く。機種に合わせて台の角度を調整。',10)
text(615,65,'ヒンジ高さの例：床から1280 / 画面中央：約1400 / 全体高さは現物調整。',10)
C.showPage()

header(2,'荷重は新しい支柱へ。MacBookは台ごと取り出す','薄い側板に本体の重さを持たせず、既存の上下フレームへ接続した枠で支える。')
title(40,PH-128,'C  側板を外した取付枠（観客側から）')
sx=.45; xx=190; yy=180
def F(u,z):return xx+u*sx,yy+z*sx
# Show local opening and broken continuation of full-height studs.
for u in [-240,200]:
    rect(*F(u,-80),40*sx,900*sx,WOOD,INK)
    for h in [-35,775]:
        a,b=F(u,h),F(u+40,h+10);line(*a,*b,white,4)
rect(*F(-200,520),400*sx,70*sx,WOOD,INK)
rect(*F(-200,-70),400*sx,70*sx,WOOD,INK)
rect(*F(-200,0),400*sx,520*sx,None,MUTED,.7)
for u in [-147,107]:
    rect(*F(u,-60),40*sx,640*sx,None,ORANGE,1)
    a=F(u+20,20);b=F(u+20,500);line(*a,*b,ORANGE,1,[3,3])
text(53,PH-190,'新しい支柱 40 × 45（断面候補）',11)
arrow(218,PH-199,*F(-220,670),MUTED)
text(53,PH-240,'上端・下端は既存フレームへ接合',11)
arrow(215,PH-249,*F(220,700),MUTED)
dim(*F(-200,0),*F(200,0),'内空幅 400',-64)
dim(*F(240,0),*F(240,520),'内空高さ 520',18)
text(53,92,'実線：新設枠 / 橙：元の中央縦材2本の位置',10)
text(53,71,'新支柱を上下へ接合した後、既存縦材を順次置き換える。',10)
notes(330,PH-230,[
    '① 新設支柱 2本',
    '既存の上下フレーム間へ通す。\n長さは現物合わせ。柱の材質を確認。',
    '② 上下の横桟 各1本',
    '断面候補45 × 70、支柱間長さ400。\n端部は金物と貫通ボルトで接合する。',
    '③ 着脱台の固定 4か所',
    '左右の受け金物へM6ボルトで固定。\n仮掛け用の受けを設け、外す途中も支持。',
    '④ 表板は独立して着脱',
    '6か所のねじで固定。表板を外すと\n本体の固定具と端子へ手が届く。'
],leading=18,size=10)

title(650,PH-128,'D  傾斜台・本体固定・取り出し')
# Section of cradle, enlarged; section diagram only, not an invented assembly view.
osx=855;osy=398;st=.49
def Q(p):return osx+p[0]*st,osy+(p[1]-1280)*st
poly([Q(p) for p in lid],SILVER,INK)
poly([Q(p) for p in body],SILVER,INK)
poly([Q(p) for p in rail],WOOD,INK)
line(908,282,908,558,MUTED,.8,[4,3])
arrow(900,431,1083,431,GREEN,2)
text(1004,449,'台ごと観客側へ抜く',11,GREEN,'center')
text(1004,412,'前方に約300 mmの作業空間',10,MUTED,'center')
arrow(1020,535,*Q((-120,1290)),MUTED)
text(1020,552,'画面を支点にしない',10,MUTED,'right')
notes(650,255,[
    '台の構成：左右の側枠＋幅の狭い底面受け2本。',
    '本体底面との間に3 mm程度のゴム当てを置く。\n面全体を板やスポンジで塞がない。',
    '前縁の小さな受けで滑りを止め、左右の本体縁を固定。\nキー・トラックパッド・画面へ締付力を掛けない。',
    '角度は125〜130°の開きに合わせて台側で調整する。\n端子位置を避けて側枠を切り欠く。',
    '取り出し：表板 → 電源・通信線 → 台固定ボルト → 台。\n配線は余長と抜止めを設け、電源アダプタは別置き。'
],leading=17,size=10)
C.showPage()

header(3,'既存モデルとの関係・部材・組立手順','元モデルの座標は確認済み。実寸の単位とMacBookの型番が未確認のため、切断寸法は現物確認後に確定する。')
title(40,PH-128,'E  中央部には、そのまま入る空間がない')
notes(40,PH-163,[
    '確認元：racing-cabinet-flow.blend / 元OBJの部材別座標',
    '中央側板の外面 X = +60.3 U。中央位置 Y = -46.3 U。',
    '既存縦材：Y = -61〜-57 U / -35.6〜-31.6 U。\n両方が今回の開口に重なる。表板だけ切っても取り付けられない。',
    '改修案：新設支柱の位置 Y = -70.3〜-66.3 U と -26.3〜-22.3 U。\n既存縦材をこの外側の支柱へ置き換える。',
    '仮に1 U = 10 mmなら、側面から170 mm内側は X = 43.3 U。\n内側の既存縦材は最大X = 43 Uなので、モデル上の差は約3 mm。',
    '170 mmの収納空間は余裕が少ない。支持材・配線を含めて実機で確認。\n深さが足りない場合は、角度変更または小さい外枠で再設計する。'
],leading=19,size=11)
text(40,343,'本図のmm寸法は新設部の提案値。モデルのUを実寸へ換算した確定図ではない。',10,ORANGE)

title(40,302,'F  部材候補')
rows=[
 ('表板','480 × 560 × t12','1','視窓400 × 270。外枠は黒＋青/ピンク'),
 ('新設支柱','断面40 × 45、長さ現物','2','既存上下枠へ接合。材料・断面は要確認'),
 ('上下横桟','400 × 70 × 45','2','金物で支柱へ接合'),
 ('傾斜台の側枠','t12合板、外形現物','2','端子の逃げと角度調整用の長穴'),
 ('底面受け','幅20〜25、長さ250目安','2','本体縁側を支え、通気孔を避ける'),
 ('固定・接合金物','M6貫通ボルト、座金、緩止め','一式','台の固定4か所。枠の接合は別途配置'),
 ('接触部','ゴム当て3 mm、小型の縁受け','一式','キーボード上を覆わない'),
]
cx=[40,126,330,366,585]; y=268
for j,h in enumerate(['部材','候補寸法 mm','数量','用途']):text(cx[j],y,h,10,MUTED)
y-=11;line(40,y,586,y,LIGHT,1)
for row in rows:
    y-=23
    for j,v in enumerate(row):text(cx[j],y,v,8.5)
    line(40,y-7,586,y-7,LIGHT,.6)

title(650,PH-128,'G  現物での組立順序')
notes(650,PH-166,[
    '1  同寸のダミーで収まりを確認',
    'Mac外形・端子位置・開く角度を測り、段ボールで再現。\n幅400 × 高さ520 × 奥行170の範囲で入るか確認する。',
    '2  既存の柱・上下枠の材質と接合を確認',
    'モデルの色・材質名だけでは実物の構造を判断できない。\n新支柱の断面と金物は現物に合わせて決める。',
    '3  仮支持 → 新支柱 → 横桟 → 既存縦材の交換',
    '上部を仮支持し、新しい支柱を上下枠へ接合してから進める。\n改修中も荷重経路を保つ。',
    '4  台を取り付け、窓寸法を実機で決める',
    '画面の四隅と観客の目線を確認してから、表板を切る。\nここに示す窓寸法は候補であり、先に切り出さない。',
    '5  配線・排熱・着脱を確認して仕上げる',
    '背面と上下を開放し、上下に空気の通り道を設ける。\nゲーム表示中の温度と、台を抜く手順を実機で確認する。'
],leading=18,size=10)
text(650,200,'設計上の残る制約',12,ORANGE)
notes(650,177,[
    '・奥まった画面は横方向から見切れやすい。窓縁は斜めに逃す。',
    '・130°まで開けられない機種では、奥行170の条件を再計算。',
    '・接合強度や転倒耐力の検証を終えた施工図ではない。'
],leading=16,size=10)
text(650,89,'参考：Apple「Macの人間工学」「Macノートブックの動作時温度」',9,MUTED)
text(650,73,'support.apple.com/ja-jp/guide/macbook-pro/apd8a47fddd1/mac',8,MUTED)
text(650,59,'support.apple.com/ja-jp/102336',8,MUTED)
C.save()
print(json.dumps({'pdf':str(OUT/'macbook-recessed-mount.pdf'),'parameters':params},ensure_ascii=False,indent=2))
