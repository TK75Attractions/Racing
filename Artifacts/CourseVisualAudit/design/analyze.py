"""Offline visual placement design. Writes only beside this script; stdlib only.

No Unity invocation and no source asset changes. Run with PYTHONDONTWRITEBYTECODE=1.
"""
from pathlib import Path
import sys, os, json, csv, math, hashlib, re, struct, zlib, array
from collections import defaultdict, Counter
from urllib.parse import unquote

OUT=Path(__file__).resolve().parent
ROOT=OUT.parents[2]
OBJ=Path('C:/Users/tsuka/AppData/Local/Temp/claude/C--Users-tsuka-Documents-GitHub-Racing/6784bfc7-1ed2-42cb-af4e-fc5e07bfcc84/scratchpad/RacingCopy/Artifacts/CourseVisualAudit/near-road-renderers.obj')
STEP=.5

def cross(a,b,c): return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
def hull(points):
    p=sorted(set(points))
    if len(p)<3:return p
    lo=[];hi=[]
    for v in p:
        while len(lo)>1 and cross(lo[-2],lo[-1],v)<=0:lo.pop()
        lo.append(v)
    for v in reversed(p):
        while len(hi)>1 and cross(hi[-2],hi[-1],v)<=0:hi.pop()
        hi.append(v)
    return lo[:-1]+hi[:-1]

def write(name,value):
    (OUT/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')

def summarize():
    target=OUT/'outlines.json'
    if target.exists():return json.loads(target.read_text(encoding='utf-8'))
    shapes={}; curr=None; tri=[]; count=0; sha=hashlib.sha256()
    def flush(s):
        if not s:return
        s['hull']=hull(s['hull'])
        s['bands']={k:hull(v) for k,v in s['bands'].items()}
    with OBJ.open('rb') as source:
        for line in source:
            sha.update(line)
            if line.startswith(b'g '):
                flush(curr)
                name=unquote(line[2:].decode('utf-8').strip())
                curr=shapes.setdefault(name,{'triangles':0,'min':[math.inf]*3,'max':[-math.inf]*3,'hull':[],'bands':{}})
                tri=[]
            elif line.startswith(b'v '):tri.append(tuple(map(float,line.split()[1:4])))
            elif line.startswith(b'f '):
                # Exporter writes three fresh vertices immediately before each f.
                fields=line.split(); count+=1
                if len(tri)!=3 or list(map(int,fields[1:]))!=[3*count-2,3*count-1,3*count]:raise ValueError('Unexpected OBJ indexing')
                curr['triangles']+=1
                xz=[(v[0],v[2]) for v in tri]
                curr['hull'].extend(xz)
                if len(curr['hull'])>4096:curr['hull']=hull(curr['hull'])
                for k in range(3):
                    curr['min'][k]=min(curr['min'][k],*(v[k] for v in tri));curr['max'][k]=max(curr['max'][k],*(v[k] for v in tri))
                # Whole triangle projection in every touched height slab encloses
                # the true clipped projection, including crossings with no vertex.
                low=math.floor(min(v[1] for v in tri)/STEP); high=math.floor(max(v[1] for v in tri)/STEP)
                for b in range(low,high+1):
                    buf=curr['bands'].setdefault(str(b),[]);buf.extend(xz)
                    if len(buf)>4096:curr['bands'][str(b)]=hull(buf)
                tri=[]
    flush(curr)
    result={'source':str(OBJ),'sha256':sha.hexdigest(),'triangles':count,'step_m':STEP,'method':'Whole triangle XZ projection in every intersected world-Y slab; convex hull. Conservative, no vertex subsampling.','shapes':shapes}
    write('outlines.json',result)
    print('Summary:',len(shapes),'objects;',count,'triangles',flush=True)
    return result

def svg(shapes,filename='baseline.svg',points=None,moves=None):
    moves=moves or {};points=points or course_points()
    sys.path.insert(0,str(ROOT/'Artifacts/CoursePlayabilityAudit/design'))
    import predict
    g=predict.build(points)
    content=['<svg xmlns="http://www.w3.org/2000/svg" viewBox="180 480 600 410" width="1500" height="1025">','<rect x="180" y="480" width="600" height="410" fill="white"/>','<g transform="translate(0,1370) scale(1,-1)">']
    groups=sorted(set(n.split('/')[1] for n in shapes))
    for n,s in shapes.items():
        dx,dz=moves.get(n,(0,0)); c=groups.index(n.split('/')[1]);color=f'hsl({c*67%360},55%,65%)'
        pts=' '.join(f'{x+dx:.3f},{z+dz:.3f}' for x,z in s['hull'])
        content.append(f'<polygon points="{pts}" fill="{color}" fill-opacity=".35" stroke="{color}" stroke-width=".15"><title>{n}</title></polygon>')
    for i,(a,b) in enumerate(zip(g['path'],g['path'][1:])):
        if g['points'][g['owners'][i]]['jump']:continue
        content.append(f'<path d="M {a[0]} {a[2]} L {b[0]} {b[2]}" stroke="#003f6b" opacity=".45" stroke-width="{g["width"][i]}"/>')
    content.append('</g>')
    for group in groups:
        ss=[s for n,s in shapes.items() if n.split('/')[1]==group]
        x=sum((s['min'][0]+s['max'][0])/2 for s in ss)/len(ss);z=sum((s['min'][2]+s['max'][2])/2 for s in ss)/len(ss)
        content.append(f'<text x="{x}" y="{1370-z}" font-size="4">{group}</text>')
    content.append('</svg>');(OUT/filename).write_text('\n'.join(content),encoding='utf-8')

def course_points():
    sys.path.insert(0,str(ROOT/'Artifacts/CoursePlayabilityAudit/design'))
    import predict
    return predict.candidate()

def png_map(shapes,name='baseline.png',points=None,moves=None):
    import colorsys
    moves=moves or {}; points=points or course_points()
    sys.path.insert(0,str(ROOT/'Artifacts/CoursePlayabilityAudit/design'));import predict
    w,h=1400,1100; scale=2.5; xmin=200;zmax=880
    pixels=[bytearray([255,255,255]*w) for _ in range(h)]
    def polygon(vs,color):
        vs=[((x-xmin)*scale,(zmax-z)*scale) for x,z in vs]
        if len(vs)<2:return
        for y in range(max(0,int(min(v[1] for v in vs))),min(h,int(max(v[1] for v in vs))+1)):
            xs=[]
            for a,b in zip(vs,vs[1:]+vs[:1]):
                if min(a[1],b[1])<=y+.5<max(a[1],b[1]):xs.append(a[0]+(b[0]-a[0])*(y+.5-a[1])/(b[1]-a[1]))
            xs.sort()
            for a,b in zip(xs[::2],xs[1::2]):
                a=max(0,int(a));b=min(w,int(b)+1)
                if b>a:pixels[y][a*3:b*3]=bytes(color)*(b-a)
    groups=sorted(set(n.split('/')[1] for n in shapes))
    for n,s in shapes.items():
        if s['max'][1]<.1:continue
        dx,dz=moves.get(n,(0,0));color=tuple(int(v*255) for v in colorsys.hsv_to_rgb(groups.index(n.split('/')[1])*0.618%1,.3,.85))
        if '/tree' in n:color=(80,160,85)
        polygon([(x+dx,z+dz) for x,z in s['hull']],color)
    g=predict.build(points);left,right=predict.road_edges(g)
    for i in range(len(left)-1):
        if g['points'][g['owners'][i]]['fit']:continue
        if g['points'][g['owners'][i]]['jump']:continue
        polygon([left[i][::2],left[i+1][::2],right[i+1][::2],right[i][::2]],(40,95,160))
    def chunk(k,b):return struct.pack('>I',len(b))+k+b+struct.pack('>I',zlib.crc32(k+b))
    data=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(b'\0'+p for p in pixels)))+chunk(b'IEND',b'')
    (OUT/name).write_bytes(data)

if __name__=='__main__':
    data=summarize(); shapes=data['shapes']
    if '--summary' in sys.argv:
        svg(shapes)
        png_map(shapes)
        for group in sorted(set(n.split('/')[1] for n in shapes)):
            ss=[s for n,s in shapes.items() if n.split('/')[1]==group]
            print(group,len(ss),[round(min(s['min'][i] for s in ss),2) for i in range(3)],[round(max(s['max'][i] for s in ss),2) for i in range(3)])
