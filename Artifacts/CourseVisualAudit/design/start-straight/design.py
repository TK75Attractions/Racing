"""Offline start-straight placement; no Unity or scene writes."""
from pathlib import Path
import sys, json, math, copy, hashlib
from urllib.parse import unquote
sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
import geometry_check as gc
import verify_plan2 as previous
predict = gc.predict

def candidate(z=805, y=3, shift=16.4):
    p = copy.deepcopy(BASE)
    p[0]['p'] = (494.7, y, z)
    p[1]['p'] = (258.2, y, z)
    return p, {'1016こうしゃ/Cube.008': (0, shift)}

BASE = json.loads((HERE/'baseline-course.json').read_text(encoding='utf-8'))

def adopted():
    p = copy.deepcopy(BASE)
    p[0]['p'] = (485.2, 3, 807)
    p[1]['p'] = (267.5, 3, 807)
    p.insert(2, copy.deepcopy(BASE[1]))
    p.append(dict(p=(494.7, 3, 797.5), w=20, fit=False, jump=False))
    return p, {'1016こうしゃ/Cube.008': (0, 23)}

def original_heading_bounds(report):
    a,b=BASE[0]['p'],BASE[1]['p']
    dx,dz=b[0]-a[0],b[2]-a[2]
    length=math.hypot(dx,dz)
    nx,nz=dz/length,-dx/length
    vertex=report['tree_witnesses'][report['witness_object']]['world_xyz_m']
    reach=report['tree_implied_min_center_z_m']-vertex[2]-10
    wall=min(nx*v[0]+nz*v[2] for v in report['north_front_wall_source_polygon_m'] if v[1]>8.99)
    lower=nx*vertex[0]+nz*vertex[2]+10+reach
    upper=wall+nz*report['maximum_north_shift_on_ground_m']-10-report['barrier_outward_reach_at_6m_m']
    return {'north_unit_normal_xz':[nx,nz],
            'minimum_north_shift_m':(lower+10+report['barrier_outward_reach_at_6m_m']-wall)/nz,
            'perpendicular_corridor_deficit_m':lower-upper,
            'method':'Original straight heading preserved. Project the tree witness and filled top of the north wall onto the same north-facing unit normal; north translation changes wall projection by nz*d.'}

def probe():
    original = gc.segments
    gc.segments = lambda p, quick=False: (lambda s,g: ([v for v in s if max(v['a'][2],v['b'][2])>775],g))(*original(p,True))
    # Full source polygons, never proximity-only old road faces.
    gc.near_faces = lambda: {}
    for y in (3,4,5,6,7,8):
        for z in (801,802,803,804,805,806,807,808,809):
            p,m = candidate(z,y)
            r=gc.check(p,m,full=True,include=lambda n:n.startswith('1016こうしゃ/Cube.008') or n.startswith('1016こうしゃ/Cube.026'))
            hits={n.split('/')[-1]:list(v) for n,v in r['hits'].items()}
            print(json.dumps({'y':y,'z':z,'hits':hits}),flush=True)

if __name__=='__main__':
    if '--probe' in sys.argv:
        probe()
    else:
        obj=Path('C:/RC/Artifacts/CourseVisualAudit/near-road-renderers.obj')
        meshes=gc.load_json('full-meshes.json')
        north=meshes['1016こうしゃ/Cube.008']
        back=meshes['1016こうしゃ/Cube.008/Cube.009']
        ground=meshes['1016こうしゃ/Plane']
        max_move=ground['max'][2]-back['max'][2]
        reach=gc.intervals()[-1][2]*6+gc.intervals()[-1][3]
        north_limit=north['min'][2]+max_move-10-reach
        witnesses={};name='';tri=[];digest=hashlib.sha256();count=0;ordinal=0
        print('Streaming original renderer OBJ for exact tree witnesses...',flush=True)
        with obj.open('rb') as f:
            for line in f:
                digest.update(line)
                if line.startswith(b'g '):
                    name=unquote(line[2:].decode('utf-8').strip());tri=[];ordinal=0
                elif line.startswith(b'v ') and name.startswith('1016こうしゃ/Cube.026/tree'):
                    tri.append(tuple(map(float,line.split()[1:4])))
                elif line.startswith(b'f ') and tri:
                    count+=1;ordinal+=1
                    if (max(v[0] for v in tri)<north['min'][0] or min(v[0] for v in tri)>north['max'][0]
                        or max(v[1] for v in tri)<3 or min(v[1] for v in tri)>9):
                        tri=[];continue
                    # Entire triangle, clipped to the common north-wall X span.
                    poly=gc.clip(tri,lambda v:v[0]-north['min'][0])
                    poly=gc.clip(poly,lambda v:north['max'][0]-v[0])
                    for lo,hi,k,b in gc.intervals():
                        band=gc.clip(poly,lambda v:v[1]-3-lo)
                        band=gc.clip(band,lambda v:3+hi-v[1])
                        for v in band:
                            required=v[2]+10+k*(v[1]-3)+b
                            if required>witnesses.get(name,{}).get('required_center_z_m',-math.inf):
                                witnesses[name]={'required_center_z_m':required,'world_xyz_m':v,'arc_height_m':v[1]-3,
                                                 'source_triangle_ordinal_in_group':ordinal,'source_triangle_world_m':tri}
                    tri=[]
        worst=max(witnesses.items(),key=lambda item:item[1]['required_center_z_m'])
        report={'scope':'Necessary corridor bounds for a horizontal east-west 20 m straight at world Y=3, with the original radius-12 barrier. Not a proof about elevated or arbitrary reroutes.',
                'obj_sha256':digest.hexdigest(),'expected_obj_sha256':gc.load_json('outlines.json')['sha256'],
                'tree_triangles_streamed':count,'ground_north_edge_m':ground['max'][2],
                'north_wing_original_back_m':back['max'][2],'maximum_north_shift_on_ground_m':max_move,
                'barrier_outward_reach_at_6m_m':reach,'north_wall_implied_max_center_z_m':north_limit,
                'tree_implied_min_center_z_m':worst[1]['required_center_z_m'],
                'incompatible_center_interval_deficit_m':worst[1]['required_center_z_m']-north_limit,
                'minimum_shift_implied_by_witness_m':worst[1]['required_center_z_m']+10+reach-north['min'][2],
                'witness_object':worst[0],'tree_witnesses':witnesses,
                'north_front_wall_source_polygon_m':min(north['polygons'],key=lambda p:sum(v[2] for v in p)/len(p)),
                'formula':'At world Y=3, tree requires center_z >= tree_z + 10 + arc_x(tree_y-3); wall requires center_z <= wall_south_z + shift - 10 - arc_x(6). The source wall is filled above its door cutouts up to Y=9.',
                'tolerance_note':'Source FBX/world coordinates have sub-millimetre numeric noise. The >4m conflict greatly exceeds it. Boundary contact is not counted as positive-area intrusion.'}
        report['obj_hash_matches_summary']=report['obj_sha256']==report['expected_obj_sha256']
        report['original_heading_preserved']=original_heading_bounds(report)
        (HERE/'corridor-conflict.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps({k:v for k,v in report.items() if k!='tree_witnesses'},ensure_ascii=False),flush=True)

