"""Reproduce the bounded candidate and its FAIL-CLOSED acceptance report.

Run from the repository root with PYTHONDONTWRITEBYTECODE=1. All writes are
confined to this directory. No Unity, package install, or production edit.
Exit 2 means a requested acceptance condition is still unsatisfied.
"""
from bounded_design import candidate
from geometry_check import *
import geometry_check as gc
import gzip

def bounds(poly):
    return [min(v[k] for v in poly) for k in range(3)],[max(v[k] for v in poly) for k in range(3)]

def rectangle_on_face(lo,hi,poly):
    """Prove an XZ rectangle is inside one simple support polygon, including
    concavities: all corners inside and no polygon boundary through the box."""
    q=[(v[0],v[2]) for v in poly]
    def inside(x,z):
        yes=False
        for a,b in zip(q,q[1:]+q[:1]):
            if (a[1]>z)!=(b[1]>z) and x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]:yes=not yes
        return yes
    if not all(inside(x,z) for x in (lo[0],hi[0]) for z in (lo[2],hi[2])):return False
    for a,b in zip(q,q[1:]+q[:1]):
        lower,upper=0.,1.
        for k,axis in [(0,0),(1,2)]:
            d=b[k]-a[k]
            if abs(d)<1e-12:
                if a[k]<lo[axis] or a[k]>hi[axis]:lower=2;break
            else:
                t0=(lo[axis]-a[k])/d;t1=(hi[axis]-a[k])/d
                lower=max(lower,min(t0,t1));upper=min(upper,max(t0,t1))
        if lower<=upper:return False
    return True

def section(height):
    radius=2*height
    pts=[(radius*math.sin(i*math.pi/48),radius*(1-math.cos(i*math.pi/48))) for i in range(17)]
    for i in range(1,17):
        angle=math.pi/3+i*(math.radians(165)-math.pi/3)/16
        pts.append((radius*math.sin(math.pi/3)+radius*.375*(math.sin(angle)-math.sin(math.pi/3)),
                    height+radius*.375*(.5-math.cos(angle))))
    return [(a[1],b[1],(b[0]-a[0])/(b[1]-a[1]),a[0]-a[1]*(b[0]-a[0])/(b[1]-a[1])) for a,b in zip(pts,pts[1:])]

def runs(ids,ss,padding=0):
    result=[]
    for i in sorted(set(ids)):
        s=ss[i];lo=s['progress']-predict.norm(predict.sub(s['b'],s['a']))/2;hi=2*s['progress']-lo
        if result and lo<=result[-1][1]+.02:result[-1][1]=max(hi,result[-1][1])
        else:result.append([lo,hi])
    if padding:
        output=[]
        for lo,hi in result:
            lo=max(0,lo-padding);hi=min(ss[-1]['progress']+1,hi+padding)
            if output and lo<=output[-1][1]:output[-1][1]=max(hi,output[-1][1])
            else:output.append([lo,hi])
        result=output
    return [[round(a,4),round(b,4)] for a,b in result]

def check_all(points,moves):
    ss,g=segments(points,False)
    for s in ss:s['max'][1]+=9.01
    outlines=load_json('outlines.json')['shapes'];meshes=load_json('full-meshes.json');near=near_faces()
    hits={}; tested=0
    # OBJ includes the actual pre-existing renderer cuts of these three floors.
    floors=('Cube.001','Plane.009','Plane.012')
    for n,obj in meshes.items():
        if n=='1016こうしゃ/Plane' or n=='1016こうしゃ/BézierCurve.001':continue
        dx,dz=move_for(n,moves)
        lo=[obj['min'][0]+dx,obj['min'][1],obj['min'][2]+dz];hi=[obj['max'][0]+dx,obj['max'][1],obj['max'][2]+dz]
        candidates=[(i,s) for i,s in enumerate(ss) if overlaps(lo,hi,s)]
        if not candidates:continue
        tree='/tree' in n
        source=near.get(n,obj['polygons']) if any(n=='1016こうしゃ/'+f or n.startswith('1016こうしゃ/'+f+'/') for f in floors) else obj['polygons']
        polys=[] if tree else [(p,*bounds(p)) for p in source]
        row={}
        for i,s in candidates:
            for zone,top in [('A',6),('B_low',3),('B_full',6),('C',9)]:
                hit=False
                if tree:
                    # Full hull is the first exclusion, never OBJ-only object bounds.
                    off=6 if zone=='C' else 0
                    if not clip_tree(obj['hull'],obj['min'][1]-off,obj['max'][1]-off,s,'A' if zone in ('A','C') else 'B',top,dx,dz):continue
                    bands=outlines.get(n,{}).get('bands',{'full':obj['hull']})
                    for band,poly in bands.items():
                        low=int(band)*.5 if band!='full' else obj['min'][1];high=low+.5 if band!='full' else obj['max'][1]
                        if clip_tree(poly,low-off,high-off,s,'A' if zone in ('A','C') else 'B',top,dx,dz):hit=True;break
                else:
                    for poly,pl,ph in polys:
                        if not overlaps([pl[0]+dx,pl[1],pl[2]+dz],[ph[0]+dx,ph[1],ph[2]+dz],s):continue
                        loc=[local(s,v,dx,dz) for v in poly]
                        if zone=='C':loc=[(u,v,h-6) for u,v,h in loc]
                        tested+=1
                        if clip_face(loc,s,'A' if zone in ('A','C') else 'B',top):hit=True;break
                if hit:row.setdefault(zone,[]).append(i)
        if row:hits[n]=row
    return ss,g,hits,tested

def main():
    inputs=[ROOT/'Assets/Editor/SchoolMapCourseSetup.cs',ROOT/'Assets/Scenes/SampleScene.unity',ROOT/'Assets/Scenes/1016こうしゃ.fbx',
            ROOT/'Assets/Managers/Barrier/RaceBarrierPath.cs',ROOT/'Assets/Managers/Barrier/RaceCourseBarriers.cs',
            ROOT/'Artifacts/CoursePlayabilityAudit/geometry.csv',OUT/'full-meshes.json',OUT/'outlines.json',OUT/'near-faces.json']
    hashes={str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in inputs}
    p,m=candidate();base=course_points();g0=predict.build(base)
    print('Checking source polygons, full bounds, tree slabs, 0.5 m segment joins...',flush=True)
    ss,g,hits,tested=check_all(p,m)
    summary={n:{z:{'intervals_m':runs(ids,ss),'segment_count':len(ids)} for z,ids in row.items()} for n,row in hits.items()}
    ids=[i for row in hits.values() for i in row.get('B_full',[])]
    critical=[i for row in hits.values() for i in row.get('B_low',[])]
    ranges=runs(ids,ss,12)
    # This is explicitly an unresolved alternative: it shrinks the protected
    # low band, not just the upper flare. It must not make strict status pass.
    barrier={'status':'conditional_alternative_not_accepted','progress_unit':'metres, raw generated-course progress, opposite race direction',
             'default':{'outwardFlare':2.5,'wallHeight':6,'minimumWallHeight':6},
             'required_generator_change':'SetWorldPath recreates default control points. Supply per-progress profile during generation; lower minimumWallHeight to 0.1 only on these paths and rebuild. No code is changed by this design.',
             'intervals':[{'progress_m':[a,b],'outwardFlare':0,'wallHeight':1.1,'transition_padding_m':12} for a,b in ranges],
             'critical_original_B_0_3_intervals_m':runs(critical,ss),
             'strict_low_band_preserved':False,
             'reason':'Height 1.1 gives a 2.3091 m full return profile. This also shrinks B below 3 m, so it is not an upper-only remedy. Do not apply without a revised requirement.',
             'conditional_inner_volume_verification':'Not used for acceptance; original B(0..3) remains the acceptance volume.'}
    write('barrier-plan2.json',barrier)
    fine=predict.build(p,.25)
    metrics={label:predict.metrics(data) for label,data in [('normal_1_5m',g),('fine_0_25m',fine)]}
    sys.path.insert(0,str(ROOT/'Artifacts/CoursePlayabilityAudit/design'));from verify_design import window_radius
    def course_accept(data):
        mm=predict.metrics(data)
        return {'length_change_percent':100*(mm['length']/g0['progress'][-1]-1),
                'axis_aligned_before_takeoff_m':predict.aligned_run(data,(584,18,598),1),
                'axis_aligned_after_landing_m':predict.aligned_run(data,(596,16,598),-1),
                'road_mesh':predict.road_mesh_metrics(data)}
    acceptance={key:course_accept(data) for key,data in [('normal_1_5m',g),('fine_0_25m',fine)]}
    tube_old=[v for v in base if v['fit']];tube_new=[v for v in p if v['fit']]
    unchanged=tube_old==tube_new
    # Compare at equal source distance inside the fitted span, rather than
    # equal lap progress (the new lap has a different source offset).
    def fit_start(points):
        j=next(i for i,v in enumerate(points) if v['fit'])
        return sum(predict.norm(predict.sub(points[i+1]['p'],points[i]['p'])) for i in range(j))
    old_fine=predict.build(base,.25);tube_deltas=[]
    for d,v,owner in zip(fine['source_distance'],fine['path'],fine['owners']):
        if not p[owner]['fit']:continue
        target=d-fit_start(p)+fit_start(base)
        j=min(len(old_fine['path'])-2,max(0,bisect_right(old_fine['source_distance'],target)-1))
        t=(target-old_fine['source_distance'][j])/(old_fine['source_distance'][j+1]-old_fine['source_distance'][j])
        tube_deltas.append(predict.norm(predict.sub(v,predict.lerp(old_fine['path'][j],old_fine['path'][j+1],t))))
    fixed_jump=[v for v in p if tuple(v['p']) in [(584,18,598),(596,16,598)]]
    gap_unchanged=all(any(v==old for v in fixed_jump) for old in base if tuple(old['p']) in [(584,18,598),(596,16,598)])
    # AABB separation is a lower bound on exact surface distance. A lower bound
    # >= .3 proves clearance without a misleading narrow-phase estimate.
    meshes=load_json('full-meshes.json');separations=[]
    for n,o in meshes.items():
        dx,dz=move_for(n,m)
        if dx==dz==0:continue
        lo=[o['min'][0]+dx,o['min'][1],o['min'][2]+dz];hi=[o['max'][0]+dx,o['max'][1],o['max'][2]+dz]
        for b,v in meshes.items():
            if tuple(move_for(b,m))==(dx,dz) or b=='1016こうしゃ/Plane':continue
            distance=math.sqrt(sum(max(0,v['min'][k]-hi[k],lo[k]-v['max'][k])**2 for k in range(3)))
            separations.append((distance,n,b))
    closest=min(separations)
    ground=meshes['1016こうしゃ/Plane'];moved=[(n,o) for n,o in meshes.items() if move_for(n,m)!=(0,0)]
    ground_inside=True;ground_deltas=[];foot_heights=[]
    for n,o in moved:
        dx,dz=move_for(n,m)
        for tx,tz in [(0,0),(dx,dz)]:
            lo=[o['min'][0]+tx,o['min'][1],o['min'][2]+tz];hi=[o['max'][0]+tx,o['max'][1],o['max'][2]+tz]
            support=next((poly for poly in ground['polygons'] if rectangle_on_face(lo,hi,poly)),None)
            if support is None:ground_inside=False;continue
            normal=predict.cross(predict.sub(support[1],support[0]),predict.sub(support[2],support[0]))
            if tx or tz:ground_deltas.append(-(normal[0]*dx+normal[2]*dz)/normal[1])
            for x in (lo[0],hi[0]):
                for z in (lo[2],hi[2]):foot_heights.append(support[0][1]-(normal[0]*(x-support[0][0])+normal[2]*(z-support[0][2]))/normal[1])
    course_ok=all(mm['min_width']>=12 and mm['max_width_rate']<=.2 and mm['heading_40m_degrees']<=6 and mm['min_radius']>=30 for mm in metrics.values()) and all(abs(v['length_change_percent'])<=3 and v['axis_aligned_before_takeoff_m']>=6 and v['road_mesh']['folded_triangles']==0 for v in acceptance.values()) and unchanged and gap_unchanged
    counts={z:sum(z in row for row in hits.values()) for z in ('A','B_low','B_full','C')}
    report={'status':'partial_not_accepted','zero_intrusion_target_met':counts['A']==counts['B_low']==0,
            'counts_objects':counts,'intrusions':summary,'course_metrics':metrics,'course_acceptance':acceptance,
            'course_hard_conditions_pass':course_ok,'baseline_length_m':g0['progress'][-1],
            'approach_5m_window_min_radius_m':window_radius(fine),
            'tube_authored_points_and_flags_unchanged':unchanged,'jump_endpoints_widths_and_flags_unchanged':gap_unchanged,
            'tube_centerline_resampling_max_delta_m':max(tube_deltas),'tube_reference_sampling_m':.25,
            'tube_original_straight_lead_in_preserved_m':predict.norm(predict.sub(p[4]['p'],p[3]['p'])),
            'moved_group_count':len(m),'moved_renderer_count':len(moved),'trees_moved':0,'maximum_displacement_m':10,
            'object_separation_lower_bound_m':closest[0],'closest_objects':closest[1:],
            'internal_relative_placement_delta_m':0,'internal_joins':'Retained as one rigid architectural unit; support plane and pre-existing within-unit contacts are not separate-object gap failures.',
            'ground':{'surface':'1016こうしゃ/Plane','plane_world_y_range_m':[ground['min'][1],ground['max'][1]],'moved_footprints_inside_filled_support_polygons':ground_inside,'old_to_new_ground_height_difference_max_m':max(map(abs,ground_deltas)),'support_height_range_under_moved_footprints_m':[min(foot_heights),max(foot_heights)],'object_y_displacement_m':0,'height_tolerance_m':.001},
            'bottleneck':{'mesh':'1016こうしゃ/Cube.047/Plane.019','west_pier_max_x_m':484.5568,'east_pier_min_x_m':505.5568,'gate_z_m':[749.91394,753.813965],
                          'clear_opening_m':21,'required_for_width_12_and_original_B_at_3_m':12+2*math.sqrt(2*12*3-3*3),
                          'deficit_m':12+2*math.sqrt(63)-21,'translation_preserves_opening':True,
                          'scope':'A local necessary-width obstruction for this school-underpass exit, not a proof that every global reroute is impossible.'},
            'method':{'source_objects':len(meshes),'face_segment_candidates':tested,'normal_course_sampling_m':1.5,'verification_subdivision_m':.5,'all_native_sample_joins_included':True,
                      'polygons':'Full FBX polygons (conservative clipping), actual existing OBJ floor cuts; full source XZ bounds/hulls, OBJ 0.5 m height slabs for trees. No OBJ-only non-tree inventory.',
                      'guard':'5 mm plus road miter enclosure; polygon crossings tested, not vertex sampling.',
                      'excluded':['Support ground Plane (below the proposed road)','Tube mesh BézierCurve.001 and fitted/tube road segments, as in the original audit']},
            'source_sha256':hashes,
            'unresolved':['Original B(0..3) is not empty; candidate must not be applied as an accepted plan.',
                          'The conditional 1.1 m barrier changes the low protected band, and is outside the upper-only remedy. Its actual barrier/rail/collider clearance and containment are not certified.',
                          'Plan 1 is not achievable through the retained 21 m exit under rigid group translations and the original barrier. No global impossibility claim is made.',
                          'Home/north widths are 12 m, below the preferred 20 m; course moves require goal/checkpoint/item re-alignment if a later design is approved.',
                          'Unity and driving/visual checks are intentionally not run.']}
    # Confirm files read by the verification itself were not changed.
    report['input_files_unchanged_during_verification']=all(hashlib.sha256(f.read_bytes()).hexdigest()==hashes[str(f.relative_to(ROOT))] for f in inputs)
    course={'status':'candidate_not_accepted','units':'world metres; full road width','source':'Assets/Editor/SchoolMapCourseSetup.cs',
            'tube_points':'Original source indices 2..122 unchanged; new indices 4..124. Two pre-tube guide points and one post-tube taper point added. All entries included.',
            'waypoints':[{'index':i,'xyz':v['p'],'width':v['w'],'jumpToNext':v['jump'],'preserveAlignment':v['fit']} for i,v in enumerate(p)],
            'authored_table':[{'index':i,'xyz':v['p'],'width':v['w'],'jumpToNext':v['jump']} for i,v in enumerate(p) if not v['fit']],
            'home_20m_priority':'Not met; 12 m proposed, 20 m cannot be robustly enclosed through the retained gate even in A with numerical guards.',
            'hard_course_conditions_pass':course_ok}
    write('moves-plan2.json',m);write('course-plan2.json',course);write('verification-plan2.json',report)
    write('remaining-C-plan2.json',{n:r['C'] for n,r in summary.items() if 'C' in r})
    write('moves-plan1.json',{})
    # Small plotting input; rendering never needs to parse the 50 MB mesh cache.
    faces=[];trees=[]
    for n,o in meshes.items():
        if n in ('1016こうしゃ/Plane','1016こうしゃ/BézierCurve.001'):continue
        dx,dz=move_for(n,m)
        if '/tree' in n:
            trees.append([[x+dx,z+dz] for x,z in o['hull']]);continue
        for poly in o['polygons']:
            low=clip(poly,lambda v:v[1]-3);low=clip(low,lambda v:9-v[1])
            if low:faces.append({'xz':[[v[0]+dx,v[2]+dz] for v in low],'kind':0})
            elif min(v[1] for v in poly)>=9 and area2([v[::2] for v in poly])>30:
                faces.append({'xz':[[v[0]+dx,v[2]+dz] for v in poly],'kind':1})
    def quads(data):
        l,r=predict.road_edges(data)
        return [[l[i][::2],l[i+1][::2],r[i+1][::2],r[i][::2]] for i in range(len(l)-1) if not data['points'][data['owners'][i]]['jump']]
    low_ids=set(critical)
    plot={'faces':faces,'trees':trees,'old':quads(g0),'new':quads(g),
          'failed_segments':[[s['a'][::2],s['b'][::2]] for i,s in enumerate(ss) if i in low_ids],
          'moved_hulls':[[[x,z+10] for x,z in o['hull']] for n,o in moved],
          'legend':'UNACCEPTED CANDIDATE: A = 0; original B (0-3 m) = 1 object; C = 4 objects'}
    (OUT/'plot-plan2.json.gz').write_bytes(gzip.compress(json.dumps(plot,separators=(',',':')).encode('utf-8'),mtime=0))
    print(json.dumps({k:report[k] for k in ('status','counts_objects','course_hard_conditions_pass','object_separation_lower_bound_m','bottleneck')},ensure_ascii=False),flush=True)
    return 0 if report['zero_intrusion_target_met'] and course_ok else 2

if __name__=='__main__':sys.exit(main())
