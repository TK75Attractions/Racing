"""Reproduce the design audit: python Artifacts/CoursePlayabilityAudit/design/verify_design.py.

No packages, Unity process, or scene writes. Prediction uses double arithmetic;
the archived Unity CSV supplies a baseline error measurement for float effects.
"""
from predict import *
import hashlib

def at(g,d):
    i=min(len(g['path'])-2,max(0,bisect.bisect_right(g['progress'],d)-1))
    t=(d-g['progress'][i])/(g['progress'][i+1]-g['progress'][i])
    return lerp(g['path'][i],g['path'][i+1],t),g['width'][i]+t*(g['width'][i+1]-g['width'][i])

def heading_run(g,limit):
    p=g['path']; ds=g['progress']; j=min(range(len(p)),key=lambda i:norm(sub(p[i],(584,18,598))))
    prior=None; turn=0.; distance=0.
    for i in range(j,len(p)-1):
        delta=sub(p[i+1],p[i]); a=math.atan2(delta[2],delta[0])
        if prior is not None:
            turn+=abs((a-prior+math.pi)%(2*math.pi)-math.pi)*180/math.pi
        if turn>limit: break
        distance=ds[i+1]-ds[j]; prior=a
    return distance

def window_radius(g):
    """Match the audit's 5m window, projecting directions to XZ.

    Include the takeoff-to-air chord join so it cannot be hidden by excluding gaps.
    """
    worst=0.
    for j in range(math.ceil(g['progress'][-1]*10)):
        d=j/10; p,_=at(g,d)
        if not (530<p[0]<=584.01 and 590<p[2]<615):continue
        directions=[]
        for offset in (-2.5,2.5):
            i=min(len(g['path'])-2,max(0,bisect.bisect_right(g['progress'],d+offset)-1))
            v=sub(g['path'][i+1],g['path'][i]);directions.append(math.atan2(v[2],v[0]))
        worst=max(worst,abs((directions[1]-directions[0]+math.pi)%(2*math.pi)-math.pi)/5)
    return 1/worst

def main():
    old=scene_points(); previous=json.loads((OUT/'previous-waypoints.json').read_text(encoding='utf-8'))
    new=candidate(); baseline=build(old); prior=build(previous); g=build(new); groups=read_obj()
    tube_end=next(i for i in range(2,len(new)) if not new[i]['fit'])
    errors=[]; width_errors=[]; edge_spans=[]
    # The CSV exports distance between interpolated edges, not sample.width.
    # In a turn the two edge vectors interpolate to a slightly shorter span.
    for j in range(len(baseline['path'])):
        before=baseline['path'][j-1 if j>0 else -2]
        after=baseline['path'][j+1 if j<len(baseline['path'])-1 else 1]
        d=sub(after,before); length=math.hypot(d[0],d[2])
        edge_spans.append(mul((d[2]/length,0,-d[0]/length),baseline['width'][j]))
    for row in csv.DictReader((OUT.parent/'geometry.csv').open(encoding='utf-8-sig')):
        distance=float(row['progress_m']); p,_=at(baseline,distance)
        errors.append(norm(sub(p,tuple(float(row[k]) for k in ('x','y','z')))))
        i=min(len(baseline['path'])-2,max(0,bisect.bisect_right(baseline['progress'],distance)-1))
        t=(distance-baseline['progress'][i])/(baseline['progress'][i+1]-baseline['progress'][i])
        width_errors.append(abs(norm(lerp(edge_spans[i],edge_spans[i+1],t))-float(row['width_m'])))
    fine_g=build(new,spacing=.25)
    m=metrics(g); fine=metrics(fine_g)
    regression=build(json.loads((OUT/'fold-regression-waypoints.json').read_text(encoding='utf-8')))
    mesh={label:road_mesh_metrics(data,rounded) for label,data,rounded in [
        ('before_fix',regression,False),('normal',g,False),('fine_0_25m',fine_g,False),
        ('float_vertices',g,True),('fine_float_vertices',fine_g,True)]}
    alignment={label:{'takeoff_m':aligned_run(data,(584,18,598),1),
                      'landing_m':aligned_run(data,(596,16,598),-1)}
               for label,data in [('normal',g),('fine_0_25m',fine_g)]}
    all_clear=clearance(g,groups,lambda name,tri:True)
    walls={n:v for n,v in all_clear.items() if any(s in n for s in ('Plane.019','平面.011','平面.012','平面.001'))}
    floors={n:v for n,v in all_clear.items() if n.split('/')[-1] in ('Plane.012','Plane.009','Cube.001')}
    fine_clear=clearance(fine_g,groups,lambda n,t:n.split('/')[-1] not in ('Plane.012','Plane.009','Cube.001'))
    other_fail={n:v for n,v in all_clear.items() if n not in floors and v['clearance']<.5}
    fine_fail={n:v for n,v in fine_clear.items() if v['clearance']<.5}
    height=[]
    for x in (508,514,520,526,530,536,540,548,554,560):
        row={'world_x':x}
        for label,data in [('old',baseline),('previous',prior),('new',g)]:
            for a,b in zip(data['path'],data['path'][1:]):
                if min(a[1],b[1])>2.9 and min(a[0],b[0])<=x<=max(a[0],b[0]) and 590<a[2]<640 and a[0]!=b[0]:
                    row[label]=lerp(a,b,(x-a[0])/(b[0]-a[0]));break
        height.append(row)
    # The added alignment point follows the takeoff in source-array order.
    roles=[('北東角',0,0),('西側接続',1,1),('着地後接続',-7,tube_end),
           ('着地',-6,tube_end+1),('踏切',-5,tube_end+2),('坂上',-4,tube_end+5),
           ('坂の接続',-3,tube_end+6),('入口手前',-2,tube_end+7),('ホーム南端',-1,tube_end+8)]
    def point_value(p):return {'xyz_m':[round(v,4) for v in p['p']],'width_m':p['w']}
    table=[{'location':label,'original':point_value(old[i]),'previous':point_value(previous[i]),
            'current':point_value(new[j])} for label,i,j in roles]
    for label,j in [('踏切前の整列点',tube_end+3),('開口の幅制御点',tube_end+4),('ホーム補助点・南',tube_end+9),('ホーム補助点・北',tube_end+10)]:
        table.append({'location':label,'original':None,'previous':None,'current':point_value(new[j])})
    table.append({'location':'チューブ全121点','original':{'width_m':12},'previous':{'width_m':12},
                  'current':{'width_m':12,'xyz_and_settings_unchanged':True}})
    # Compare retained tube at equal SOURCE distance, not equal global progress:
    # global distance sampling shifts slightly after changing the rest of the lap.
    old_source_offset=norm(sub(old[1]['p'],old[0]['p']))+norm(sub(old[2]['p'],old[1]['p']))
    new_source_offset=norm(sub(new[1]['p'],new[0]['p']))+norm(sub(new[2]['p'],new[1]['p']))
    tube_deltas=[]
    for d,p,owner in zip(g['source_distance'],g['path'],g['owners']):
        if not new[owner]['fit']:continue
        target=d-new_source_offset+old_source_offset
        i=min(len(baseline['path'])-2,max(0,bisect.bisect_right(baseline['source_distance'],target)-1))
        t=(target-baseline['source_distance'][i])/(baseline['source_distance'][i+1]-baseline['source_distance'][i])
        tube_deltas.append(norm(sub(p,lerp(baseline['path'][i],baseline['path'][i+1],t))))
    report={
        'metrics':m,'fine_0_25m_metrics':fine,'audit_5m_min_horizontal_radius_including_gap_edge_m':window_radius(g),
        'road_mesh':mesh,'jump_axis_alignment_within_0_5_degrees':alignment,
        'straight_1e_5_degrees_m':heading_run(g,1e-5),'heading_within_2_degrees_m':heading_run(g,2),
        'baseline_error_max_m':max(errors),'baseline_error_rms_m':math.sqrt(sum(v*v for v in errors)/len(errors)),
        'baseline_edge_width_error_max_m':max(width_errors),
        'walls_conservative_clearance':walls,'other_nonfloor_failures':other_fail,
        'fine_0_25m_nonfloor_clearance':fine_clear,'fine_0_25m_nonfloor_failures':fine_fail,
        'floors_require_terrain_rebake':floors,'height_profile':height,
        'tube_waypoints_unchanged':len(old[2:-7])==len(new[2:tube_end]) and all(norm(sub(a['p'],b['p']))<1e-8 and all(a[k]==b[k] for k in ('w','jump','fit')) for a,b in zip(old[2:-7],new[2:tube_end])),
        'tube_resampling_max_delta_m':max(tube_deltas),
        'gap_endpoints_unchanged':all(norm(sub(old[i]['p'],new[j]['p']))<.001 for i,j in [(-6,tube_end+1),(-5,tube_end+2)]),
        'input_sha256':{str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in [OUT.parent/'near-road-colliders.obj',OUT.parent/'geometry.csv',OUT/'source-waypoints.json',OUT/'previous-waypoints.json',OUT/'fold-regression-waypoints.json',ROOT/'Assets/Editor/SchoolMapCourseSetup.cs',ROOT/'Assets/Managers/RaceCourse.cs',ROOT/'Assets/Managers/RaceCourseLineSmoothing.cs',ROOT/'Assets/Managers/RaceCourseRoad.cs',OUT/'predict.py',OUT/'verify_design.py']},
        'scope':'Road curvature excludes the roadless jump chord. Approach X=530..584, Z=590..615. Heading change over the last 40m uses generated 3D progress; jump-axis alignment length uses generated horizontal distance. All surface collider triangles around the entire lap, including the tube, takeoff and landing, must have cross-product Y>0 after production degeneracy filtering. Clearance covers ALL non-tube road segments with any endpoint Y>2.9, including north/west connections and landing, vertically 0..2.5 m; 3 mm numerical guard and conservative miter enclosure. Both 1.5 m and 0.25 m tessellations checked; old floor cuts are reported separately.',
        'limits':['OBJ contains exported collider triangles, not independent renderer triangles. Shared FBX wall geometry is the visual-clearance proxy; confirm in Unity.',
                  'Floors must be recut from source FBX by TubeCoursePreparation. The OBJ contains the old floor cuts; it cannot prove the newly rebaked visual floor.',
                  'The mesh port uses double arithmetic with an additional float32 vertex check; it is not a bit-exact Unity float simulation. Apply and run TubeCourseValidation in Unity to confirm generated meshes, barriers and jumps.',
                  'Unchanged tube control points/settings preserve its alignment function, but global uniform tessellation shifts samples; interpolation differences are reported separately.']}
    nonfloor={n:v for n,v in all_clear.items() if n not in floors}
    report['result']={
        'control_points_original_previous_current':table,
        'acceptance_predictions':{
            'wall_clearance_min_m':min(v['clearance'] for v in nonfloor.values()),
            'fine_wall_clearance_min_m':min(v['clearance'] for v in fine_clear.values()),
            'home_and_corner_authored_width_m':min(new[j]['w'] for j in (0,1,tube_end+8,tube_end+9,tube_end+10)),
            'home_straight_generated_min_width_m':min(w for w,owner in zip(g['width'],g['owners']) if owner in (0,tube_end+8,tube_end+9,tube_end+10)),
            'entrance_authored_width_m':new[tube_end+4]['w'],
            'takeoff_authored_width_m':new[tube_end+2]['w'],
            'landing_width_m':new[tube_end+1]['w'],
            'after_landing_authored_width_m':new[tube_end]['w'],
            'tube_width_m':12,
            'heading_40m_degrees':m['heading_40m_degrees'],
            'fine_heading_40m_degrees':fine['heading_40m_degrees'],
            'straight_m':report['straight_1e_5_degrees_m'],
            'axis_aligned_before_takeoff_min_m':min(v['takeoff_m'] for v in alignment.values()),
            'axis_aligned_after_landing_min_m':min(v['landing_m'] for v in alignment.values()),
            'folded_road_triangles':max(v['folded_triangles'] for k,v in mesh.items() if k!='before_fix'),
            'min_horizontal_radius_m':m['min_radius'],
            'fine_min_horizontal_radius_m':fine['min_radius'],
            'audit_5m_radius_including_gap_edge_m':report['audit_5m_min_horizontal_radius_including_gap_edge_m'],
            'max_width_rate_m_per_m':m['max_width_rate'],
            'fine_max_width_rate_m_per_m':fine['max_width_rate'],
            'min_width_m':min(m['min_width'],fine['min_width']),
            'gap_endpoints_unchanged':report['gap_endpoints_unchanged'],
            'tube_waypoints_unchanged':report['tube_waypoints_unchanged']}}
    (OUT/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    with (OUT/'predicted-geometry.csv').open('w',newline='',encoding='utf-8') as f:
        writer=csv.writer(f);writer.writerow(['progress_m','source_distance_m','x','y','z','width_m','segment','road'])
        for i,p in enumerate(g['path']):writer.writerow([g['progress'][i],g['source_distance'][i],*p,g['width'][i],g['owners'][i],not new[g['owners'][i]]['jump']])
    assert report['baseline_error_max_m']<.005 and report['baseline_edge_width_error_max_m']<.005
    assert mesh['before_fix']['folded_triangles']==1 and mesh['before_fix']['takeoff_folded_triangles']==1
    assert all(v['triangle_count']>0 and v['folded_triangles']==0 for k,v in mesh.items() if k!='before_fix')
    assert all(v['takeoff_m']>=6 and v['landing_m']>=6 for v in alignment.values())
    assert m['heading_40m_degrees']<=6 and m['min_radius']>=30
    assert report['audit_5m_min_horizontal_radius_including_gap_edge_m']>=30
    assert fine['heading_40m_degrees']<=6 and fine['min_radius']>=30
    assert m['min_width']>=12 and fine['min_width']>=12 and m['max_width_rate']<=.2 and fine['max_width_rate']<=.2
    assert len([n for n in groups if any(s in n for s in ('Plane.019','平面.011','平面.012','平面.001'))])==4
    assert walls and not other_fail and not fine_fail and min(v['clearance'] for v in walls.values())>=.5
    assert report['tube_waypoints_unchanged'] and report['gap_endpoints_unchanged']
    assert all(new[j]['w']==20 for j in (0,1,tube_end+8,tube_end+9,tube_end+10))
    assert new[tube_end+2]['w']==18 and new[tube_end+1]['w']==20 and new[tube_end+4]['w']>=12
    report['result']['status']='pass'
    # Write pass only after every acceptance assertion succeeds.
    (OUT/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['metrics','fine_0_25m_metrics','audit_5m_min_horizontal_radius_including_gap_edge_m','straight_1e_5_degrees_m','heading_within_2_degrees_m','baseline_error_max_m','tube_resampling_max_delta_m','other_nonfloor_failures']},indent=2))

if __name__=='__main__':main()
