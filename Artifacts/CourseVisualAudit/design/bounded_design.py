"""Bounded, campus-preserving design candidate. Not an acceptance certificate."""
from geometry_check import *

def candidate():
    p=course_points()
    p[0].update(p=(494,6,800),w=12)
    p[1].update(p=(245,1,800),w=12)
    p[127]['p']=(549,17.7,599.53816)
    p[128]['p']=(536,15.5,600.42658)
    p[130]['w']=14
    p[131].update(p=(496,3,619),w=12)
    p[132].update(p=(489.5,3,650),w=12)
    p[133].update(p=(494,3,720),w=12)
    p.insert(124,dict(p=(607.5,12,598),w=17.93,jump=False,fit=False))
    # Preserve the final >35 m of the original tube approach, including its
    # height. Thus both smoothing kernels see the original lead-in inside the
    # tube; simply moving the long western link would alter the tube entrance.
    t=(796.7-595)/(796.7-557)
    lead=(258.2+(281.90006-258.2)*t,3+(5.0000067-3)*t,595)
    p[2:2]=[dict(p=(260,3,640),w=12,jump=False,fit=False),dict(p=lead,w=12,jump=False,fit=False)]
    return p,{'1016こうしゃ/Cube.008':[0,10]}

if __name__=='__main__':
    p,m=candidate()
    # Full source polygons are required when a course leaves the old OBJ's
    # 25 m proximity envelope. Keep the existing, already-cut floor meshes.
    old=near_faces()
    import geometry_check as gc
    gc.near_faces=lambda:{n:v for n,v in old.items() if any(n=='1016こうしゃ/'+k or n.startswith('1016こうしゃ/'+k+'/') for k in ('Cube.001','Plane.009','Plane.012'))}
    r=check(p,m,top=3,full=True,quick=False,include=lambda n:not n.startswith('1016こうしゃ/BézierCurve.001') and n!='1016こうしゃ/Plane')
    write('bounded-check.json',r);write('bounded-points.json',p)
    print(json.dumps(r,ensure_ascii=False),flush=True)
