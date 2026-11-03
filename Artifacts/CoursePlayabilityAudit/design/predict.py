"""Offline port of RaceCourse / RaceCourseLineSmoothing. No Unity invocation.

Run with Python 3 (standard library only) from the repository root.
The scene is read only to obtain the existing tube waypoints and course transform.
"""
import bisect
import csv
import json
import math
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
def add(a,b): return tuple(x+y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def mul(a,t): return tuple(x*t for x in a)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def norm(a): return math.sqrt(dot(a,a))
def lerp(a,b,t): return add(a,mul(sub(b,a),t))
def smooth(t): return max(0,min(1,t))**2*(3-2*max(0,min(1,t)))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])


def road_edges(g):
    """RaceCourse.BuildOffsetPaths, including the closed-loop seam tangent."""
    p=g['path']; left=[]; right=[]; fallback=(1.,0.,0.)
    for i,center in enumerate(p):
        before=p[i-1 if i>0 else -2]; after=p[i+1 if i<len(p)-1 else 1]
        tangent=sub(after,before); lateral=cross((0,1,0),tangent); length=norm(lateral)
        lateral=mul(lateral,1/length) if length>1e-8 else fallback
        fallback=lateral; offset=mul(lateral,g['width'][i]/2)
        left.append(sub(center,offset)); right.append(add(center,offset))
    return left,right


def road_triangles(g, float_vertices=False):
    """RaceCourseRoad.Build/AddTriangle: all surface collider triangles.

    Preserve winding, outgoing segment ownership and production degeneracy
    cutoffs. Tube shoulders, paint and barriers are separate meshes.
    """
    left,right=road_edges(g)
    if float_vertices:
        # Additional precision sensitivity check, not a full Unity float port.
        def f32(v):return tuple(struct.unpack('f',struct.pack('f',x))[0] for x in v)
        left=list(map(f32,left)); right=list(map(f32,right))
    for i in range(len(g['path'])-1):
        if g['points'][g['owners'][i]]['jump'] or dot(sub(g['path'][i+1],g['path'][i]),sub(g['path'][i+1],g['path'][i]))<1e-8:continue
        for side,tri in enumerate(((left[i],left[i+1],right[i]),(right[i],left[i+1],right[i+1]))):
            normal=cross(sub(tri[1],tri[0]),sub(tri[2],tri[0]))
            if dot(normal,normal)>=1e-8:yield i,side,tri,normal


def road_mesh_metrics(g, float_vertices=False):
    result={'triangle_count':0,'folded_triangles':0,'takeoff_folded_triangles':0,'landing_folded_triangles':0,
            'min_normal_y':1.,'min_cross_y':float('inf'),'worst_triangle':None}
    for i,side,tri,normal in road_triangles(g,float_vertices):
        result['triangle_count']+=1; ny=normal[1]/norm(normal)
        result['min_cross_y']=min(result['min_cross_y'],normal[1])
        if ny<result['min_normal_y']:
            result['min_normal_y']=ny
            result['worst_triangle']={'sample':i,'side':side,'vertices':tri,'normal':mul(normal,1/norm(normal))}
        if normal[1]<=0:
            result['folded_triangles']+=1
            for label,edge in [('takeoff',(584,18,598)),('landing',(596,16,598))]:
                if min(norm(sub(g['path'][j],edge)) for j in (i,i+1))<3:result[label+'_folded_triangles']+=1
    return result


def aligned_run(g,edge,step,limit=.5):
    """Conservative generated horizontal length within limit of the jump axis.

    Count complete segments from the fixed edge, stopping at the first failing
    heading. step=1 walks back from takeoff, -1 walks forward from landing.
    """
    p=g['path']; j=min(range(len(p)),key=lambda i:norm(sub(p[i],edge))); distance=0.
    for i in (range(j,len(p)-1) if step==1 else range(j-1,-1,-1)):
        delta=sub(p[i+1],p[i]); angle=math.degrees(math.atan2(abs(delta[2]),abs(delta[0])))
        if angle>limit:break
        distance+=math.hypot(delta[0],delta[2])
    return distance

def scene_points():
    snapshot=OUT/'source-waypoints.json'
    if snapshot.exists(): return json.loads(snapshot.read_text(encoding='utf-8'))
    text = (ROOT/'Assets/Scenes/SampleScene.unity').read_text(encoding='utf-8-sig')
    block = text.split('  waypoints:\n')[1].split('  closedLoop:')[0]
    rows = re.findall(r'position: \{x: ([^,]+), y: ([^}]+)\}\s+height: (\S+)\s+curve: (\S+)\s+width: (\S+)\s+jumpToNext: (\S+)\s+preserveAlignment: (\S+)',block)
    # RaceCourse is a root transform with identity rotation/scale in this export.
    result=[dict(p=(float(x)+500.0001,float(y)+3,float(z)+500.00006),w=float(w),jump=j=='1',fit=f=='1') for x,z,y,c,w,j,f in rows]
    snapshot.write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result

def build(points, spacing=1.5):
    source=[]; widths=[]; owners=[]
    for i,a in enumerate(points):
        b=points[(i+1)%len(points)]
        for step in range(0 if i==0 else 1,21):
            t=step/20
            u=0 if step==0 else 1 if step==20 else (1+math.cos((1-t)*math.pi))/2
            source.append(lerp(a['p'],b['p'],u)); widths.append(a['w']+(b['w']-a['w'])*smooth(t)); owners.append(i)
    owners[:-1]=owners[1:]
    origin=source[0]; source=[sub(p,origin) for p in source]
    ds=[0.]; ints=[(0.,0.,0.)]
    for a,b in zip(source,source[1:]):
        length=norm(sub(b,a)); ds.append(ds[-1]+length); ints.append(add(ints[-1],mul(add(a,b),length/2)))
    total=ds[-1]
    def index(d): return min(len(ds)-2,max(0,bisect.bisect_right(ds,d)-1))
    def integral(d):
        cycle=math.floor(d/total); d-=cycle*total
        i=index(d); part=d-ds[i]; length=ds[i+1]-ds[i]
        return add(mul(ints[-1],cycle),add(ints[i],mul(add(source[i],lerp(source[i],source[i+1],part/length)),part/2)))
    def avg(d,r,fallback): return fallback if r<.001 else mul(sub(integral(d+r),integral(d-r)),1/(2*r))
    fits=[]; gaps=[]; prev=False; ingap=False
    for i in range(len(source)-1):
        fit=points[owners[i]]['fit']; gap=points[owners[i]]['jump']
        if fit!=prev: fits.append(ds[i])
        if gap!=ingap: gaps.append(ds[i])
        prev=fit; ingap=gap
    if prev: fits.append(total)
    if ingap: gaps.append(total)
    count=math.ceil(total/max(.25,spacing)); samples=[total*i/count for i in range(count+1)]
    for edge in gaps: samples=[d for d in samples if abs(d-edge)>=.0001]; samples.append(edge)
    path=[]; ws=[]; segs=[]; rawds=[]
    for d in sorted(samples):
        if rawds and d-rawds[-1]<.0001: continue
        i=index(d); t=(d-ds[i])/(ds[i+1]-ds[i]); owner=owners[i]
        p=lerp(source[i],source[i+1],t)
        nearest=min((min(abs(d-e),total-abs(d-e)) for e in fits),default=total)
        fitted=0 if points[owner]['fit'] else nearest
        radius=16 if points[owner]['fit'] else 16+19*smooth(nearest/35)
        gaplimit=min((min(abs(d-e),total-abs(d-e)) for e in gaps),default=total)
        h=p if points[owner]['jump'] else avg(d,min(35,radius,gaplimit),p)
        v=p if points[owner]['jump'] else avg(d,min(35-10*smooth(fitted/35),gaplimit),p)
        path.append(add((h[0],v[1],h[2]),origin)); ws.append(widths[i]+(widths[i+1]-widths[i])*t); segs.append(owner); rawds.append(d)
    path[-1]=path[0]; ws[-1]=ws[0]
    progress=[0.]
    for a,b in zip(path,path[1:]): progress.append(progress[-1]+norm(sub(b,a)))
    return dict(path=path,width=ws,owners=segs,progress=progress,source_distance=rawds,points=points)

def read_obj():
    vs=[]; groups={}; group=''
    for line in (OUT.parent/'near-road-colliders.obj').read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('v '): vs.append(tuple(map(float,line.split()[1:4])))
        elif line.startswith('g '): group=line[2:]; groups.setdefault(group,[])
        elif line.startswith('f '): groups[group].append(tuple(vs[int(i.split('/')[0])-1] for i in line.split()[1:4]))
    return groups

def clip(poly, value):
    if not poly: return []
    result=[]; a=poly[-1]; da=value(a)
    for b in poly:
        db=value(b)
        if (da>=0)!=(db>=0): result.append(lerp(a,b,da/(da-db)))
        if db>=0: result.append(b)
        a=b; da=db
    return result

def clearance(g, groups, select):
    """Continuous segment slabs, grade-corrected 0..2.5m band, linear width.

    Signed lateral distance from the road edge; negative means penetration.
    Uses chord-normal edges (actual miter-edge difference reported separately).
    """
    result={}
    for name,tris in groups.items():
        tris=[tri for tri in tris if select(name,tri)]
        if not tris: continue
        best=1e9; where=None
        lo=tuple(min(v[k] for tri in tris for v in tri) for k in range(3)); hi=tuple(max(v[k] for tri in tris for v in tri) for k in range(3))
        for i,(a,b) in enumerate(zip(g['path'],g['path'][1:])):
            # Cover every above-ground, non-tube road segment, including the
            # north/west connection and landing. The jump has no road surface.
            owner=g['points'][g['owners'][i]]
            if owner['fit'] or owner['jump'] or max(a[1],b[1])<=2.9: continue
            delta=sub(b,a); length=math.hypot(delta[0],delta[2])
            if length<1e-8: continue
            f=(delta[0]/length,0,delta[2]/length); r=(f[2],0,-f[0]); slope=delta[1]/length
            w=g['width'][i]/2; dw=(g['width'][i+1]-g['width'][i])/(2*length)
            # The production offsets use the average neighbouring tangent rather
            # than this chord's normal. Enclose the resulting entire road quad,
            # including its height plane, with a conservative endpoint error.
            def right(j):
                before=g['path'][j-1 if j>0 else -2]; after=g['path'][j+1 if j<len(g['path'])-1 else 1]
                d=sub(after,before); l=math.hypot(d[0],d[2]); return (d[2]/l,0,-d[0]/l)
            error=max(norm(sub(mul(right(j),g['width'][j]/2),mul(r,g['width'][j]/2))) for j in (i,i+1))
            reach=max(w,g['width'][i+1]/2)+3+error
            if any(max(a[k],b[k])+reach<lo[k] or min(a[k],b[k])-reach>hi[k] for k in (0,2)): continue
            guard=error*abs(slope)+.003
            if min(a[1],b[1])-guard>hi[1] or max(a[1],b[1])+2.5+guard<lo[1]: continue
            for tri in tris:
                loc=[(dot(sub(v,a),f),dot(sub(v,a),r),v[1]-a[1]-slope*dot(sub(v,a),f)) for v in tri]
                poly=clip(loc,lambda v:v[0]+error); poly=clip(poly,lambda v:length+error-v[0]); poly=clip(poly,lambda v:v[2]+guard); poly=clip(poly,lambda v:2.5+guard-v[2])
                if not poly: continue
                # Each side is affine; also split at lateral zero to handle walls crossing the road.
                for sign in (-1,1):
                    side=clip(poly,lambda v:sign*v[1])
                    for v in side:
                        c=sign*v[1]-(w+dw*v[0])-error*(1+abs(dw))
                        if c<best: best=c; where=add(a,add(mul(f,v[0]),add(mul(r,v[1]),(0,slope*v[0]+v[2],0))))
        if where: result[name]={'clearance':best,'at':where}
    return result

def metrics(g):
    p=g['path']; ds=g['progress']; angles=[math.atan2(b[2]-a[2],b[0]-a[0]) for a,b in zip(p,p[1:])]
    curv=[]
    gapowner=g['owners'][min(range(len(p)),key=lambda j:norm(sub(p[j],(596,16,598))))]
    for i in range(1,len(p)-1):
        turn=abs((angles[i]-angles[i-1]+math.pi)%(2*math.pi)-math.pi)
        dl=(math.hypot(*sub(p[i+1],p[i])[::2])+math.hypot(*sub(p[i],p[i-1])[::2]))/2
        # A jump has no driving surface. Curvature is defined on road-road
        # intervals only; report the road-to-air heading discontinuity separately.
        if g['owners'][i-1]!=gapowner and g['owners'][i]!=gapowner and 530<p[i][0]<584.001 and 590<p[i][2]<615: curv.append((turn/dl,p[i]))
    takeoff=min(range(len(p)),key=lambda i:norm(sub(p[i],(584,18,598))))
    heading=sum(abs((angles[i]-angles[i-1]+math.pi)%(2*math.pi)-math.pi) for i in range(takeoff+1,len(angles)) if ds[i]-ds[takeoff]<=40)
    rates=[abs(b-a)/(ds[i+1]-ds[i]) for i,(a,b) in enumerate(zip(g['width'],g['width'][1:])) if ds[i+1]-ds[i]>1e-8]
    mismatch=abs((angles[takeoff]-angles[takeoff-1]+math.pi)%(2*math.pi)-math.pi)*180/math.pi
    return dict(length=ds[-1],min_width=min(g['width']),max_width_rate=max(rates),heading_40m_degrees=heading*180/math.pi,min_radius=1/max(k for k,_ in curv),worst_curve=max(curv),takeoff=p[takeoff],road_to_gap_heading_degrees=mismatch)

def candidate():
    # Parse the authored ground points AND widths, rather than hard-code a
    # candidate that could silently disagree with SchoolMapCourseSetup.
    source=(ROOT/'Assets/Editor/SchoolMapCourseSetup.cs').read_text(encoding='utf-8-sig')
    authored=[tuple(float(v.strip().rstrip('f')) for v in xyz.split(',')) for xyz in re.findall(r'new Vector3\(([\d.f, ]+)\)',source)]
    width_block=re.search(r'var groundWidths = new\[\]\s*\{([^}]+)\}',source)
    widths=[float(v.strip().rstrip('f')) for v in width_block[1].split(',')]
    assignment=re.search(r'i < 2 \? ([\d.]+)f :\s*i < tubeEnd \? ([\d.]+)f : groundWidths\[i - tubeEnd\]',source)
    assert assignment and len(authored)==len(widths)+2, 'Update parser if the setup layout changes.'
    assert 'i == tubeEnd + 1;' in source and 'i >= 2 && i < tubeEnd;' in source
    home_width,tube_width=map(float,assignment.groups())
    tube=scene_points()[2:-7]
    assert all(p['w']==tube_width and p['fit'] for p in tube)
    home=[dict(p=p,w=home_width,jump=False,fit=False) for p in authored[:2]]
    ground=[dict(p=p,w=w,jump=i==1,fit=False) for i,(p,w) in enumerate(zip(authored[2:],widths))]
    return home+tube+ground

if __name__=='__main__':
    p=scene_points(); g=build(p)
    print('points',len(p),'length',g['progress'][-1], 'tail',p[-7:])
    groups=read_obj()
    for name,tris in groups.items():
        if any(s in name for s in ['Plane.019','平面.011','平面.012','平面.001','Cube.001','Plane.009']):
            vs=[v for tri in tris for v in tri]
            print(name,len(tris),'min',tuple(min(v[k] for v in vs) for k in range(3)),'max',tuple(max(v[k] for v in vs) for k in range(3)))
    # Baseline comparison to the supplied 0.5 m export (rounded to millimetres).
    errs=[]
    for row in csv.DictReader((OUT.parent/'geometry.csv').open(encoding='utf-8-sig')):
        d=float(row['progress_m']); i=min(len(g['path'])-2,bisect.bisect_right(g['progress'],d)-1)
        t=(d-g['progress'][i])/(g['progress'][i+1]-g['progress'][i]); v=lerp(g['path'][i],g['path'][i+1],t)
        errs.append(norm(sub(v,tuple(float(row[k]) for k in ['x','y','z']))))
    print('baseline max/RMS error m',max(errs),math.sqrt(sum(e*e for e in errs)/len(errs)))
    g=build(candidate())
    print('candidate',json.dumps(metrics(g)))
    def select(name,tri): return any(s in name for s in ['Plane.019','平面.011','平面.012','平面.001'])
    print(json.dumps(clearance(g,groups,select),ensure_ascii=True,indent=2))
