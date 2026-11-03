"""Validate the adopted north-apron layout against the preserved I2 baseline."""
from verify import *

def corner(g, west=False):
    if not west:return corner_metrics(g)
    rows=[]
    for i in range(1,len(g['path'])-1):
        at=g['path'][i]
        if at[0]>310 or at[2]<755:continue
        a=predict.sub(at,g['path'][i-1]);b=predict.sub(g['path'][i+1],at)
        turn=abs((math.atan2(b[2],b[0])-math.atan2(a[2],a[0])+math.pi)%(2*math.pi)-math.pi)
        if turn>1e-9:rows.append(((math.hypot(a[0],a[2])+math.hypot(b[0],b[2]))/2/turn,at))
    radius,at=min(rows)
    return {'minimum_pointwise_horizontal_radius_m':radius,'at_world_xyz_m':at}

def main():
    p,m=adopted();metrics={}
    setup=(gc.ROOT/'Assets/Editor/SchoolMapCourseSetup.cs').read_text(encoding='utf-8-sig')
    controls=[tuple(map(float,v)) for v in gc.re.findall(r'new Vector3\(([\d.]+)f, ([\d.]+)f, ([\d.]+)f\)',setup)]
    assert controls[:3]==[tuple(v['p']) for v in p[:3]]
    assert controls[3:]==[tuple(v['p']) for v in p[-12:]]
    for label,spacing in [('normal_1_5m',1.5),('fine_0_25m',.25)]:
        baseline=predict.build(BASE,spacing);g=predict.build(p,spacing)
        row=predict.metrics(g)
        row.update(length_change_percent=100*(row['length']/baseline['progress'][-1]-1),
                   northeast_corner=corner(g),west_corner=corner(g,True),
                   baseline_northeast_corner=corner(baseline),baseline_west_corner=corner(baseline,True),
                   all_ground_corners=corner_metrics(g,False),baseline_all_ground_corners=corner_metrics(baseline,False),
                   road_mesh=predict.road_mesh_metrics(g),road_mesh_float_vertices=predict.road_mesh_metrics(g,True))
        metrics[label]=row
    original=gc.segments
    gc.segments=lambda p,quick=False:(lambda s,g:([v for v in s if max(v['a'][2],v['b'][2])>755],g))(*original(p,False))
    gc.near_faces=lambda:{}
    target=lambda n:n.startswith('1016こうしゃ/Cube.008') or n.startswith('1016こうしゃ/Cube.026')
    intrusions=gc.check(p,m,full=True,include=target)['hits']
    building=building_check(23)
    ground=gc.load_json('full-meshes.json')['1016こうしゃ/Plane']
    north=max(v[2] for f in ground['polygons'] for v in f)
    strips=[]
    for face in ground['polygons']:
        normal=predict.cross(predict.sub(face[1],face[0]),predict.sub(face[2],face[0]))
        delta=(0,-normal[2]/normal[1]*8,8)
        for a,b in zip(face,face[1:]+face[:1]):
            if abs(a[2]-north)<.001 and abs(b[2]-north)<.001:
                strips.append([a,b,predict.add(b,delta),predict.add(a,delta)])
    # Merge the coplanar original north boundary with the apron for filled-polygon support.
    faces=copy.deepcopy(ground['polygons'])
    for face in faces:
        normal=predict.cross(predict.sub(face[1],face[0]),predict.sub(face[2],face[0]))
        delta=(0,-normal[2]/normal[1]*8,8)
        for i,v in enumerate(face):
            if abs(v[2]-north)<.001:face[i]=predict.add(v,delta)
    for row in building['ground_support']:
        lo,hi=row['moved_bounds_m'];face=next((f for f in faces if previous.rectangle_on_face(lo,hi,f)),None)
        row['footprint_inside_extended_filled_polygon']=face is not None
        if face:
            normal=predict.cross(predict.sub(face[1],face[0]),predict.sub(face[2],face[0]))
            heights=[face[0][1]-(normal[0]*(x-face[0][0])+normal[2]*(z-face[0][2]))/normal[1]
                     for x in (lo[0],hi[0]) for z in (lo[2],hi[2])]
            row['extended_ground_heights_m']=heights
            row['maximum_ground_height_change_m']=max(abs(a-b) for a,b in zip(heights,row['old_ground_heights_m']))
    building['all_footprints_on_extended_ground']=all(v['footprint_inside_extended_filled_polygon'] for v in building['ground_support'])
    accepted=not intrusions and building['separation_pass'] and building['all_footprints_on_extended_ground'] and all(
        r['max_width_rate']<=.2 and r['min_width']>=12 and abs(r['length_change_percent'])<=3 and
        r['northeast_corner']['minimum_pointwise_horizontal_radius_m']>=25 and
        r['west_corner']['minimum_pointwise_horizontal_radius_m']>=25 and
        not r['road_mesh']['folded_triangles'] and not r['road_mesh_float_vertices']['folded_triangles']
        for r in metrics.values())
    result=dict(status='accepted_offline' if accepted else 'failed',control_points=p,moves=m,
                extension_north_m=8,original_ground_north_m=north,extended_ground_north_m=north+8,
                support_north_margin_m=north+8-max(v['moved_bounds_m'][1][2] for v in building['ground_support']),
                added_support_polygons=strips,intrusions=intrusions,metrics=metrics,building=building,
                original_western_leadin_and_tube_jump_landing_controls_unchanged=p[2:-1]==BASE[1:],
                scope='North straight and both corners (generated segment has endpoint Z>755); complete building polygons, conservative whole-tree height-band hulls. Zero hull hits proves triangle exclusion. Existing short-scale radius issues outside the changed corners are reported, not claimed repaired.',
                unity_validation='Pending; no Unity invocation by this script.')
    result['source_sha256']={n:hashlib.sha256((gc.ROOT/n).read_bytes()).hexdigest() for n in [
        'Assets/Editor/SchoolMapCourseSetup.cs','Assets/Editor/CourseSceneryLayout.cs',
        'Assets/Editor/TubeCoursePreparation.cs','Artifacts/CourseVisualAudit/design/full-meshes.json',
        'Artifacts/CourseVisualAudit/design/start-straight/baseline-course.json']}
    write('adopted-verification.json',result)
    print(json.dumps({k:v for k,v in result.items() if k in ['status','support_north_margin_m','intrusions','original_western_leadin_and_tube_jump_landing_controls_unchanged']},ensure_ascii=False))
    return 0 if accepted else 2

if __name__=='__main__':sys.exit(main())
