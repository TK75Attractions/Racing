"""Reproduce placement candidates and fail-closed acceptance; no scene writes."""
from design import *

def write(name, data):
    (HERE/name).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')

def corner_metrics(g, northeast_only=True):
    rows=[]
    p=g['path']
    for i in range(len(p)-1):
        before=i-1 if i else len(p)-2
        if any(g['points'][g['owners'][j]]['fit'] or g['points'][g['owners'][j]]['jump'] for j in (before,i)):
            continue
        if northeast_only and not (p[i][0]>450 and p[i][2]>750):
            continue
        a=predict.sub(p[i],p[before]);b=predict.sub(p[i+1],p[i])
        turn=abs((math.atan2(b[2],b[0])-math.atan2(a[2],a[0])+math.pi)%(2*math.pi)-math.pi)
        length=(math.hypot(a[0],a[2])+math.hypot(b[0],b[2]))/2
        if turn>1e-9:rows.append((length/turn,p[i]))
    radius,at=min(rows)
    return {'minimum_pointwise_horizontal_radius_m':radius,'at_world_xyz_m':at,
            'scope':('Home-straight northeast corner, X>450 and Z>750' if northeast_only else 'All non-tube road-road joins')+'; includes the closed seam; excludes the roadless jump.'}

def building_check(shift):
    meshes=gc.load_json('full-meshes.json'); moves={'1016こうしゃ/Cube.008':(0,shift)}
    ground=meshes['1016こうしゃ/Plane'];separations=[];support=[]
    for n,o in meshes.items():
        dx,dz=gc.move_for(n,moves)
        if dx==dz==0:continue
        lo=[o['min'][0]+dx,o['min'][1],o['min'][2]+dz]
        hi=[o['max'][0]+dx,o['max'][1],o['max'][2]+dz]
        for b,v in meshes.items():
            if gc.move_for(b,moves)!=(0,0) or b=='1016こうしゃ/Plane':continue
            distance=math.sqrt(sum(max(0,v['min'][k]-hi[k],lo[k]-v['max'][k])**2 for k in range(3)))
            separations.append((distance,n,b))
        heights=[]
        for tz in (0,shift):
            ll=[o['min'][0],o['min'][1],o['min'][2]+tz]
            hh=[o['max'][0],o['max'][1],o['max'][2]+tz]
            face=next((p for p in ground['polygons'] if previous.rectangle_on_face(ll,hh,p)),None)
            if face is None:
                heights.append(None);continue
            normal=predict.cross(predict.sub(face[1],face[0]),predict.sub(face[2],face[0]))
            heights.append([face[0][1]-(normal[0]*(x-face[0][0])+normal[2]*(z-face[0][2]))/normal[1]
                            for x in (ll[0],hh[0]) for z in (ll[2],hh[2])])
        support.append({'renderer':n,'moved_bounds_m':[lo,hi],
                        'old_ground_heights_m':heights[0],'new_ground_heights_m':heights[1],
                        'footprint_inside_ground_filled_polygon':heights[1] is not None,
                        'ground_height_change_max_m':max(abs(a-b) for a,b in zip(*heights)) if all(heights) else None})
    closest=min(separations)
    return {'shift_north_m':shift,'moved_renderer_count':len(support),'trees_moved':0,
            'minimum_separation_aabb_lower_bound_m':closest[0],'closest_objects':closest[1:],
            'separation_pass':closest[0]>=.3,'object_y_rotation_scale_changes':False,
            'all_footprints_on_ground':all(r['footprint_inside_ground_filled_polygon'] for r in support),
            'ground_support':support,
            'scope':'Other school source renderers; excludes the supporting Plane and contacts internal to the rigid moved wing. AABB distance is a conservative lower bound.'}

def main():
    write('baseline-course.json',BASE)
    original=gc.segments
    # Include native sample joins and subdivisions <=0.5m. Test the straight
    # and BOTH connecting corners, not just the middle of the straight.
    gc.segments=lambda p,quick=False:(lambda s,g:([v for v in s if max(v['a'][2],v['b'][2])>775],g))(*original(p,False))
    gc.near_faces=lambda:{}  # Use complete FBX polygons outside old OBJ coverage.
    base_g=predict.build(BASE)
    reports=[]
    for z,shift in [(801,16.4),(801.5,16.4),(802,16.4),(804,16.4),(805.5,16.4),(805.5,21),(807,23)]:
        p,m=candidate(z,3,shift)
        print('Checking z=',z,'north shift=',shift,flush=True)
        r=gc.check(p,m,full=True,include=lambda n:n.startswith('1016こうしゃ/Cube.008') or n.startswith('1016こうしゃ/Cube.026'))
        normal=predict.build(p);fine=predict.build(p,.25)
        metrics={}
        for label,g in [('normal_1_5m',normal),('fine_0_25m',fine)]:
            row=predict.metrics(g)
            row.update(length_change_percent=100*(row['length']/base_g['progress'][-1]-1),
                       northeast_corner=corner_metrics(g),all_ground_corners=corner_metrics(g,False),
                       road_mesh=predict.road_mesh_metrics(g),
                       road_mesh_float_vertices=predict.road_mesh_metrics(g,True))
            metrics[label]=row
        reports.append({'straight_endpoints_m':[p[0]['p'],p[1]['p']],
                        'width_m':20,'building_north_shift_m':shift,'intrusions':r['hits'],
                        'zero_intrusion_target_met':not r['hits'],'metrics':metrics,
                        'building':building_check(shift),
                        'tube_control_points_width_flags_unchanged':[v for v in p if v['fit']]==[v for v in BASE if v['fit']],
                        'jump_and_landing_connections_unchanged':p[2:]==BASE[2:]})
    sources=['Assets/Editor/SchoolMapCourseSetup.cs','Assets/Scenes/SampleScene.unity',
             'Assets/Scenes/1016こうしゃ.fbx','Assets/Managers/RaceCourse.cs',
             'Assets/Managers/RaceCourseLineSmoothing.cs','Assets/Managers/RaceCourseRoad.cs',
             'Artifacts/CoursePlayabilityAudit/design/predict.py',
             'Artifacts/CourseVisualAudit/design/full-meshes.json',
             'Artifacts/CourseVisualAudit/design/full-bounds.json',
             'Artifacts/CourseVisualAudit/design/outlines.json']
    result={'status':'blocked_no_accepted_layout','production_control_points_changed':False,
            'baseline_length_m':base_g['progress'][-1],
            'baseline_northeast_corner':corner_metrics(base_g),'candidates':reports,
            'baseline_all_ground_corners':{label:corner_metrics(predict.build(BASE,spacing),False)
                                           for label,spacing in [('normal_1_5m',1.5),('fine_0_25m',.25)]},
            'source_sha256':{n:hashlib.sha256((gc.ROOT/n).read_bytes()).hexdigest() for n in sources},
            'limits':['Candidate tree hits use conservative 0.5m height-band hulls. Zero proves exclusion; a positive hull hit is not by itself an exact mesh intersection.',
                      'corridor-conflict.json supplies an independent exact OBJ triangle witness, without relying on tree hull false positives.',
                      'The course retains world Y=3. Elevated or underground routes have not been authorized or adopted.',
                      'The unchanged tube and jump fields describe authored control points only. Generated tube/landing resampling and preservation of the original western lead-in have not been certified for these rejected candidates.',
                      'predict.metrics min_radius and heading fields describe the jump approach only; all_ground_corners and northeast_corner are separate full-ground/corner measurements.',
                      'Unity was not launched. No scene, terrain, barrier or runtime asset was modified.']}
    write('verification.json',result)
    print(json.dumps({'status':result['status'],'candidates':[
        {'z':r['straight_endpoints_m'][0][2],'shift':r['building_north_shift_m'],
         'hit_objects':len(r['intrusions']),'ground_ok':r['building']['all_footprints_on_ground'],
         'corner_radius':r['metrics']['fine_0_25m']['northeast_corner']['minimum_pointwise_horizontal_radius_m']}
        for r in reports]},ensure_ascii=False),flush=True)
    return 2

if __name__=='__main__':
    sys.exit(main())
