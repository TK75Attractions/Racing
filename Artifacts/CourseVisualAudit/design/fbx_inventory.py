"""Read-only binary FBX metadata inventory (no SDK or Unity needed)."""
from pathlib import Path
import struct,zlib,array,json,math,itertools
OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[2]
F=ROOT/'Assets/Scenes/1016こうしゃ.fbx'

def inventory():
    dest=OUT/'fbx-inventory.json'
    if dest.exists():return json.loads(dest.read_text(encoding='utf-8'))
    with F.open('rb') as f:
        assert f.read(23)==b'Kaydara FBX Binary  \x00\x1a\x00'
        version=struct.unpack('<I',f.read(4))[0]; wide=version>=7500
        def prop(keep=True):
            t=f.read(1).decode('ascii')
            if t in 'YCFDIL':
                fm={'Y':'h','C':'?','F':'f','D':'d','I':'i','L':'q'}[t];return struct.unpack('<'+fm,f.read(struct.calcsize(fm)))[0]
            if t in 'SR':
                n=struct.unpack('<I',f.read(4))[0];v=f.read(n);return v.decode('utf-8',errors='replace') if t=='S' else None
            if t in 'fdlibc':
                n,enc,nb=struct.unpack('<III',f.read(12));b=f.read(nb)
                if not keep:return None
                if enc:b=zlib.decompress(b)
                a=array.array({'f':'f','d':'d','l':'q','i':'i','b':'b','c':'b'}[t]);a.frombytes(b);return a
            raise ValueError(t)
        def node():
            h=f.read(25 if wide else 13)
            if not h or not any(h):return None
            end,np,nb,nl=struct.unpack('<QQQB' if wide else '<IIIB',h)
            name=f.read(nl).decode('utf-8');ps=[prop(name=='Vertices') for _ in range(np)];kids=[]
            if name in ('LayerElementNormal','LayerElementUV','LayerElementMaterial','LayerElementSmoothing','LayerElementColor','Layer','AnimationCurve'):
                f.seek(end);return (name,ps,[])
            while f.tell()<end:
                child=node()
                if child is None:break
                kids.append(child)
            f.seek(end);return (name,ps,kids)
        top=[]
        while True:
            x=node()
            if x is None:break
            top.append(x)
    objects=next(n for n in top if n[0]=='Objects')[2]
    cons=next(n for n in top if n[0]=='Connections')[2]
    models={};geos={}
    for name,ps,kids in objects:
        if name=='Model':
            properties={p[1][0]:p[1][4:] for k in kids if k[0]=='Properties70' for p in k[2]}
            models[str(ps[0])]={'name':ps[1].split('\x00')[0],'kind':ps[2],'properties':properties}
        elif name=='Geometry':
            vs=next((k[1][0] for k in kids if k[0]=='Vertices'),None)
            if vs is not None:
                geos[str(ps[0])]={'name':ps[1].split('\x00')[0],'count':len(vs)//3,'min':[min(vs[i::3]) for i in range(3)],'max':[max(vs[i::3]) for i in range(3)]}
    connections=[n[1] for n in cons if n[0]=='C']
    for c in connections:
        if c[0]=='OO' and str(c[1]) in models:models[str(c[1])]['parent']=str(c[2])
        elif c[0]=='OO' and str(c[1]) in geos and str(c[2]) in models:models[str(c[2])]['geometry']=str(c[1])
    result={'version':version,'models':models,'geometries':geos}
    dest.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    return result

def mm(a,b):return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
def ident():return [[float(i==j) for j in range(4)] for i in range(4)]
def trs(p):
    t=ident();s=ident()
    for i,v in enumerate(p.get('Lcl Translation',[0,0,0])):t[i][3]=v
    for i,v in enumerate(p.get('Lcl Scaling',[1,1,1])):s[i][i]=v
    r=ident()
    for axis,angle in enumerate(p.get('Lcl Rotation',[0,0,0])):
        a=math.radians(angle);rr=ident();i=(axis+1)%3;j=(axis+2)%3
        rr[i][i]=rr[j][j]=math.cos(a);rr[i][j]=-math.sin(a);rr[j][i]=math.sin(a);r=mm(rr,r)
    return mm(t,mm(r,s))
def world_bounds(d):
    models=d['models'];cache={};paths={}
    root=ident();root[0][0]=-.03;root[1][1]=root[2][2]=.03;root[0][3]=671.9;root[2][3]=860
    def visit(i):
        if i in cache:return cache[i]
        m=models[i];parent=m.get('parent','0');a=root if parent=='0' else visit(parent)
        cache[i]=mm(a,trs(m['properties']));paths[i]=('1016こうしゃ' if parent=='0' else paths[parent])+'/'+m['name']
        return cache[i]
    result={}
    for i,m in models.items():
        a=visit(i)
        if 'geometry' not in m:continue
        g=d['geometries'][m['geometry']]
        vs=[tuple(sum(a[k][j]*v[j] for j in range(3))+a[k][3] for k in range(3)) for v in itertools.product(*zip(g['min'],g['max']))]
        result[paths[i]]={'model_id':i,'min':[min(v[k] for v in vs) for k in range(3)],'max':[max(v[k] for v in vs) for k in range(3)],'matrix':a,'corners':vs}
    (OUT/'full-bounds.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    return result

if __name__=='__main__':
    d=inventory();b=world_bounds(d);print('models',len(d['models']),'geometries',len(d['geometries']))
    for n,s in b.items():
        if n.count('/')==1 or n.endswith('tree.016'):
            print(n,[round(v,3) for v in s['min']],[round(v,3) for v in s['max']])
