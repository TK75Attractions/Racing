"""Complete FBX mesh inventory supplementing proximity-only OBJ triangles.

Stores full non-tree polygons, preserving n-gons instead of assuming a fan is
the imported triangulation. Full tree XZ hulls and vertical bounds suffice for
conservative exclusion. OBJ triangles remain authoritative near the road.
"""
from fbx_inventory import *
from analyze import hull,write

def extract():
    dest=OUT/'full-meshes.json'
    if dest.exists():return json.loads(dest.read_text(encoding='utf-8'))
    d=inventory();bounds=world_bounds(d);wanted={m.get('geometry') for m in d['models'].values()};geos={}
    with F.open('rb') as f:
        f.seek(23);wide=struct.unpack('<I',f.read(4))[0]>=7500
        def prop(keep):
            t=f.read(1).decode('ascii')
            if t in 'YCFDIL':
                fm={'Y':'h','C':'?','F':'f','D':'d','I':'i','L':'q'}[t];return struct.unpack('<'+fm,f.read(struct.calcsize(fm)))[0]
            if t in 'SR':
                n=struct.unpack('<I',f.read(4))[0];b=f.read(n);return b.decode('utf-8',errors='replace') if t=='S' else None
            n,enc,nb=struct.unpack('<III',f.read(12));b=f.read(nb)
            if not keep:return None
            if enc:b=zlib.decompress(b)
            a=array.array({'f':'f','d':'d','l':'q','i':'i','b':'b','c':'b'}[t]);a.frombytes(b);return a
        def node(parent=''):
            h=f.read(25 if wide else 13)
            if not h or not any(h):return None
            end,np,nb,nl=struct.unpack('<QQQB' if wide else '<IIIB',h);name=f.read(nl).decode('utf-8')
            ps=[prop(name in ('Vertices','PolygonVertexIndex')) for _ in range(np)];kids=[]
            if name not in ('Objects','Geometry') and parent!='Geometry':f.seek(end);return (name,ps,[])
            while f.tell()<end:
                child=node(name)
                if child is None:break
                kids.append(child)
            f.seek(end)
            if name=='Geometry':
                v=next((k[1][0] for k in kids if k[0]=='Vertices'),None);ind=next((k[1][0] for k in kids if k[0]=='PolygonVertexIndex'),None)
                if v is not None:geos[str(ps[0])]=(v,ind)
                return (name,ps,[])
            return (name,ps,kids)
        while node() is not None:pass
    result={}
    for n,b in bounds.items():
        m=d['models'][b['model_id']];vs,indices=geos[m['geometry']];a=b['matrix']
        world=[tuple(sum(a[k][j]*vs[i+j] for j in range(3))+a[k][3] for k in range(3)) for i in range(0,len(vs),3)]
        lo=[min(v[k] for v in world) for k in range(3)];hi=[max(v[k] for v in world) for k in range(3)]
        s={'min':lo,'max':hi,'hull':hull((v[0],v[2]) for v in world),'polygons':[]}
        if '/tree' not in n:
            p=[]
            for idx in indices:
                p.append(world[idx if idx>=0 else -idx-1])
                if idx<0:s['polygons'].append(p);p=[]
        result[n]=s
    write('full-meshes.json',result)
    return result

if __name__=='__main__':
    d=extract();print('Full meshes',len(d),'polygons',sum(len(s['polygons']) for s in d.values()))
    print('tree016',next((s['min'],s['max']) for n,s in d.items() if n.endswith('tree.016')))
