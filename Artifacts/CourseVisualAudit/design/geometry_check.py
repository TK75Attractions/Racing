"""Conservative offline polygon/height-envelope placement checker."""
from analyze import *
sys.path.insert(0,str(ROOT/'Artifacts/CoursePlayabilityAudit/design'))
import predict
from bisect import bisect_right
from functools import lru_cache

@lru_cache(None)
def load_json(name):return json.loads((OUT/name).read_text(encoding='utf-8'))

@lru_cache(None)
def near_faces():
    path=OUT/'near-faces.json'
    if path.exists():return json.loads(path.read_text(encoding='utf-8'))
    result={};name='';tri=[]
    with OBJ.open('r',encoding='utf-8-sig') as f:
        for line in f:
            if line.startswith('g '):name=unquote(line[2:].strip());tri=[]
            elif '/tree' in name:continue
            elif line.startswith('v '):tri.append(tuple(map(float,line.split()[1:])))
            elif line.startswith('f '):result.setdefault(name,[]).append(tri);tri=[]
    write('near-faces.json',result)
    return result

def clip(poly,field):
    if not poly:return []
    out=[];a=poly[-1];fa=field(a)
    for b in poly:
        fb=field(b)
        if (fa>=0)!=(fb>=0):
            t=fa/(fa-fb);out.append(tuple(x+(y-x)*t for x,y in zip(a,b)))
        if fb>=0:out.append(b)
        a=b;fa=fb
    return out

def area2(poly):return abs(sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(poly,poly[1:]+poly[:1]))) if len(poly)>=3 else 0
def area3(poly):
    if len(poly)<3:return 0
    return sum(predict.norm(predict.cross(predict.sub(poly[i],poly[0]),predict.sub(poly[i+1],poly[0]))) for i in range(1,len(poly)-1))

def segments(points,quick=False):
    g=predict.build(points);left,right=predict.road_edges(g);result=[]
    def at(d):
        i=min(len(g['path'])-2,max(0,bisect_right(g['progress'],d)-1));t=(d-g['progress'][i])/(g['progress'][i+1]-g['progress'][i])
        return predict.lerp(g['path'][i],g['path'][i+1],t),g['width'][i]+t*(g['width'][i+1]-g['width'][i]),g['owners'][i],predict.lerp(predict.sub(right[i],g['path'][i]),predict.sub(right[i+1],g['path'][i+1]),t)
    ds=g['progress'] if quick else sorted(set([i*.5 for i in range(math.ceil(g['progress'][-1]/.5))]+g['progress']+[g['progress'][-1]]))
    for d0,d1 in zip(ds,ds[1:]):
        if d1-d0<1e-7:continue
        a,w,owner,ra=at(d0);b,wb,ob,rb=at(d1)
        if points[owner]['jump'] or points[owner]['fit']:continue
        delta=predict.sub(b,a);length=math.hypot(delta[0],delta[2]);f=(delta[0]/length,delta[2]/length);r=(f[1],-f[0]);slope=delta[1]/length
        e=max(math.hypot(ra[0]-r[0]*w/2,ra[2]-r[1]*w/2),math.hypot(rb[0]-r[0]*wb/2,rb[2]-r[1]*wb/2))+.005
        reach=max(w,wb)/2+11+e
        result.append({'a':a,'b':b,'f':f,'r':r,'length':length,'w':w/2,'dw':(wb-w)/length/2,'slope':slope,'e':e,'progress':(d0+d1)/2,
                       'min':[min(a[0],b[0])-reach,min(a[1],b[1])-.01,min(a[2],b[2])-reach],
                       'max':[max(a[0],b[0])+reach,max(a[1],b[1])+6.01,max(a[2],b[2])+reach]})
    return result,g

def intervals(top=6,radius=12):
    # Same sixteen piecewise linear lower-arc facets as RaceBarrierPath.
    p=[(radius*math.sin(math.pi*i/48),radius*(1-math.cos(math.pi*i/48))) for i in range(17)]
    out=[]
    for (x,y),(xx,yy) in zip(p,p[1:]):
        low=max(0,y);hi=min(top,yy)
        if hi<=low:continue
        slope=(xx-x)/(yy-y);intercept=x-slope*y
        out.append((low,hi,slope,intercept))
    return out

def local(s,v,dx=0,dz=0):
    x=v[0]+dx-s['a'][0];z=v[2]+dz-s['a'][2];u=x*s['f'][0]+z*s['f'][1]
    return (u,x*s['r'][0]+z*s['r'][1],v[1]-s['a'][1]-s['slope']*u)

def clip_face(poly,s,zone,top):
    e=s['e'];hguard=.005+e*abs(s['slope']);length=s['length'];w=s['w'];dw=s['dw']
    p=clip(poly,lambda v:v[0]+e);p=clip(p,lambda v:length+e-v[0])
    if not p:return False
    for h0,h1,k,b in ([(0,top,0,.5)] if zone=='A' else intervals(top)):
        q=clip(p,lambda v:v[2]-h0+hguard);q=clip(q,lambda v:h1+hguard-v[2])
        if not q:continue
        for sign in (-1,1):
            r=clip(q,lambda v:w+dw*v[0]+k*v[2]+b+e-sign*v[1])
            r=clip(r,lambda v:(w+dw*v[0]+e+sign*v[1]) if zone=='A' else sign*v[1]-(w+dw*v[0])+e)
            if area3(r)>1e-8:return True
    return False

def clip_tree(outline,lo,hi,s,zone,top,dx,dz):
    e=s['e'];hg=.005+e*abs(s['slope']);w=s['w'];dw=s['dw'];a=s['a'];sl=s['slope']
    p=[local(s,(x,0,z),dx,dz)[:2] for x,z in outline]
    p=clip(p,lambda v:v[0]+e);p=clip(p,lambda v:s['length']+e-v[0])
    for h0,h1,k,b in ([(0,top,0,.5)] if zone=='A' else intervals(top)):
        q=clip(p,lambda v:hi-a[1]-sl*v[0]-h0+hg)
        q=clip(q,lambda v:a[1]+sl*v[0]+h1-lo+hg)
        if not q:continue
        for sign in (-1,1):
            r=clip(q,lambda v:w+dw*v[0]+k*(h1+hg)+b+e-sign*v[1])
            r=clip(r,lambda v:w+dw*v[0]+k*(hi-a[1]-sl*v[0]+hg)+b+e-sign*v[1])
            r=clip(r,lambda v:w+dw*v[0]+e+sign*v[1] if zone=='A' else sign*v[1]-w-dw*v[0]+e)
            if area2(r)>1e-8:return True
    return False

def move_for(n,moves):
    # Desired WORLD displacement relative to immutable baseline. Most specific
    # group wins. Application must snapshot first and place parents before children.
    keys=[p for p in moves if n==p or n.startswith(p+'/')]
    return moves[max(keys,key=len)] if keys else (0,0)

def overlaps(lo,hi,s):return all(lo[k]<=s['max'][k] and hi[k]>=s['min'][k] for k in range(3))

def check(points,moves,top=6,full=False,include=None,quick=False):
    ss,g=segments(points,quick);data=load_json('outlines.json')['shapes']
    meshes=load_json('full-meshes.json')
    objects=meshes if full else {n:meshes[n] for n in data if n in meshes}
    # Generated render-chunk object is represented by its full FBX parent.
    objects.update({n:meshes[n] for n in meshes if any(p.startswith(n+'/Render Chunks') for p in data)})
    faces=near_faces()
    hits={};tested=0
    for n,obj in objects.items():
        if include is not None and not include(n):continue
        if obj['max'][1]<.1:continue
        dx,dz=move_for(n,moves);lo=[obj['min'][0]+dx,obj['min'][1],obj['min'][2]+dz];hi=[obj['max'][0]+dx,obj['max'][1],obj['max'][2]+dz]
        candidates=[s for s in ss if overlaps(lo,hi,s)]
        if not candidates:continue
        tree='/tree' in n;src=data.get(n);row={}
        for s in candidates:
            for zone,ztop in [('A',6),('B',top)]:
                hit=False
                if tree:
                    bands=src['bands'] if src else {'full':obj['hull']}
                    for band,p in bands.items():
                        low=int(band)*.5 if band!='full' else obj['min'][1];high=low+.5 if band!='full' else obj['max'][1]
                        if high<s['min'][1] or low>s['max'][1]:continue
                        if clip_tree(p,low,high,s,zone,ztop,dx,dz):hit=True;break
                else:
                    for poly in faces.get(n,obj['polygons']):
                        if not overlaps([min(v[k] for v in poly)+(dx if k==0 else dz if k==2 else 0) for k in range(3)], [max(v[k] for v in poly)+(dx if k==0 else dz if k==2 else 0) for k in range(3)],s):continue
                        tested+=1
                        if clip_face([local(s,v,dx,dz) for v in poly],s,zone,ztop):hit=True;break
                if hit:row.setdefault(zone,[]).append(round(s['progress'],3))
        if row:hits[n]={zone:{'min_progress_m':min(ds),'max_progress_m':max(ds),'sample_segments':len(ds)} for zone,ds in row.items()}
    return {'hits':hits,'objects':len(objects),'face_segment_candidates':tested,'course_metrics':predict.metrics(g)}

if __name__=='__main__':
    p=course_points();moves={}
    if '--candidate' in sys.argv:
        p[0]['p']=(482.7,3,810.6693);p[1]['p']=(258.2,3,821.7)
        p[-1]['p']=(482.7,3,720);p[-2]['p']=(482.7,3,650)
        moves={'1016こうしゃ/Cube.008':(0,30)}
    result=check(p,moves,top=3 if '--low' in sys.argv else 6,full='--full' in sys.argv)
    write('candidate-check.json',result)
    print(json.dumps(result,ensure_ascii=False,indent=2))
