"""Offline Plan B design; all writes confined to this directory. No Unity."""
from pathlib import Path
import sys, json, math, hashlib, bisect, gzip
HERE=Path(__file__).resolve().parent
sys.dont_write_bytecode=True
sys.path.insert(0,str(HERE.parent))
import geometry_check as gc
import verify_plan2 as prev
from bounded_design import candidate as bounded
predict=gc.predict
ROOT=gc.ROOT
def write(name,data):
    (HERE/name).write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')

def candidate():
    p,m=bounded()
    base=gc.course_points()
    for v in p:
        if v['fit']:continue
        match=next((q for q in base if tuple(q['p'])==tuple(v['p'])),None)
        if match:v['w']=match['w']
    p[0]['w']=20
    p[1]['w']=20
    p[0]['p']=(495.0568,6,799)
    p[1]['p']=(245,1,799)
    for v in p:
        if v['p'][2] in (619,650,720) and v['p'][0]<500:v.update(w=18)
    p[-1]['p']=(495.0568,3,720)
    p[-2]['p']=(495.0568,3,650)
    p[-3]['p']=(493,3,619)
    p[130]['p']=(549,17.7,599.6)
    p[131]['p']=(536,15.5,600.45)
    p[126]['w']=17.95
    m={next(iter(m)):[0,5]}
    return p,m

def profile_kind(progress):
    if progress<=210 or progress>=1720:return 'SlimUpright'
    if progress<240 or progress>1690:return 'Blend'
    return 'DefaultArc'

def slim_weight(progress):
    if progress<=210 or progress>=1720:return 1.
    if progress<240:return 1-predict.smooth((progress-210)/30)
    if progress>1690:return predict.smooth((progress-1690)/30)
    return 0.

def check(points,moves,zones=('A','B_full','C'),new_profile=False):
    ss,g=gc.segments(points,False)
    for s in ss:
        s['max'][1]+=9.01
        for axis in (0,2):
            s['min'][axis]-=1+3*s['e'];s['max'][axis]+=1+3*s['e']
    shapes=gc.load_json('outlines.json')['shapes'];meshes=gc.load_json('full-meshes.json');near=gc.near_faces()
    hits={};tested=0
    floors=('Cube.001','Plane.009','Plane.012')
    for n,obj in meshes.items():
        if n in ('1016こうしゃ/Plane','1016こうしゃ/BézierCurve.001'):continue
        dx,dz=gc.move_for(n,moves)
        lo=[obj['min'][0]+dx,obj['min'][1],obj['min'][2]+dz];hi=[obj['max'][0]+dx,obj['max'][1],obj['max'][2]+dz]
        candidates=[(i,s) for i,s in enumerate(ss) if gc.overlaps(lo,hi,s)]
        if not candidates:continue
        tree='/tree' in n
        source=near.get(n,obj['polygons']) if any(n=='1016こうしゃ/'+f or n.startswith('1016こうしゃ/'+f+'/') for f in floors) else obj['polygons']
        polys=[] if tree else [(p,*prev.bounds(p)) for p in source]
        row={}
        for i,base_s in candidates:
            for zone in zones:
                s=base_s
                kind=profile_kind(s['progress'])
                half_span=predict.norm(predict.sub(s['b'],s['a']))/2
                if any(s['progress']-half_span<hi and s['progress']+half_span>lo for lo,hi in [(210,240),(1690,1720)]):kind='Blend'
                if zone=='WallEnvelope' and kind=='DefaultArc':continue
                # Slim B (maximum inner reach .1 m) is strictly inside the
                # separately verified A shoulder (.5 m). Its complete solid
                # envelope, including cap, rails and .1 m thickness, <= .2 m.
                # Still clip B explicitly, using conservative rectangular slabs.
                saved_intervals=gc.intervals
                if new_profile and zone in ('B_full','WallEnvelope'):
                    if kind=='SlimUpright':gc.intervals=lambda top:[(0,top,0,.1)]
                    elif kind=='Blend':
                        minimum_slim=min(slim_weight(s['progress']-half_span),slim_weight(s['progress']+half_span))
                        reach=11.01*(1-minimum_slim)+.1*minimum_slim
                        gc.intervals=lambda top:[(0,top,0,reach)]
                top=9 if zone=='C' else 6
                if zone=='WallEnvelope':
                    minimum_slim=min(slim_weight(s['progress']-half_span),slim_weight(s['progress']+half_span))
                    reach=11.61*(1-minimum_slim)+.2*minimum_slim
                    top=13.2*(1-minimum_slim)+6.2*minimum_slim
                    gc.intervals=lambda top:[(0,top,0,reach)]
                if new_profile and zone in ('B_full','WallEnvelope'):
                    bound_reach=reach if zone=='WallEnvelope' or kind=='Blend' else (.1 if kind=='SlimUpright' else 11.01)
                    # Road-edge miter displacement also bounds orientation
                    # error of the supplied cross-band normal. Enclose its
                    # longitudinal sweep at the profile's outermost point.
                    min_half=min(s['w'],s['w']+s['dw']*s['length'])
                    s=dict(s,e=s['e']*(1+bound_reach/max(.01,min_half)))
                hit=False
                if tree:
                    off=6 if zone=='C' else 0
                    if not gc.clip_tree(obj['hull'],obj['min'][1]-off,obj['max'][1]-off,s,'A' if zone in ('A','C') else 'B',top,dx,dz):
                        gc.intervals=saved_intervals
                        continue
                    for band,poly in shapes.get(n,{}).get('bands',{'full':obj['hull']}).items():
                        low=int(band)*.5 if band!='full' else obj['min'][1];high=low+.5 if band!='full' else obj['max'][1]
                        if gc.clip_tree(poly,low-off,high-off,s,'A' if zone in ('A','C') else 'B',top,dx,dz):hit=True;break
                else:
                    for poly,pl,ph in polys:
                        if not gc.overlaps([pl[0]+dx,pl[1],pl[2]+dz],[ph[0]+dx,ph[1],ph[2]+dz],s):continue
                        loc=[gc.local(s,v,dx,dz) for v in poly]
                        if zone=='C':loc=[(u,v,h-6) for u,v,h in loc]
                        tested+=1
                        if gc.clip_face(loc,s,'A' if zone in ('A','C') else 'B',top):hit=True;break
                if hit:row.setdefault(zone,[]).append(i)
                gc.intervals=saved_intervals
        if row:hits[n]=row
    return ss,g,hits,tested

def main():
    p,m=candidate()
    zones=('A',) if '--probe' in sys.argv else ('A','B_full','C')
    ss,g,hits,tested=check(p,m,zones)
    summary={n:{z:prev.runs(ids,ss) for z,ids in row.items()} for n,row in hits.items()}
    result={'metrics':predict.metrics(g),'hits':summary,'tested':tested}
    write('probe.json' if '--probe' in sys.argv else 'raw.json',result)
    print(json.dumps(result,ensure_ascii=False),flush=True)

def sections(length):
    return {'schema_version':1,'units':'world metres; widths are full widths',
        'progress_convention':'raw cumulative 3D centreline distance from generated sample 0; opposite race direction; NOT edge arclength or authored source_distance',
        'course_length_m':length,'side':'both','default_profile':'DefaultArc',
        'profiles':{
            'DefaultArc':{'wallHeight':6,'minimumWallHeight':6,'outwardFlare':2.5,'radius_m':12,'colliderThickness_m':.6,'top_height_m':6+4.5*(.5-math.cos(math.radians(165)))},
            'SlimUpright':{'vertical_height_m':6,'base_round_radius_m':.1,'top_round_radius_m':.1,'top_height_m':6.1,'solid_top_height_m':6.2,'maximum_inner_outward_m':.1,'colliderThickness_m':.1,'maximum_outer_rail_and_collider_envelope_m':.2,'lower_sections':16,'upper_sections':16,
                'base':'j=0..4: theta=j*pi/8; x=.1*sin(theta), h=.1*(1-cos(theta))',
                'vertical':'j=5..16: x=.1, h=.1+5.9*(j-4)/12',
                'cap':'j=17..32: phi=(j-16)*pi/32; x=.1*cos(phi), h=6+.1*sin(phi)'}},
        'intervals':[
            {'progress_m':[0,210],'profile':'SlimUpright','continuation':'same core as [1720,L] across closed seam'},
            {'progress_m':[210,240],'profile':'Blend','from':'SlimUpright','to':'DefaultArc','blend':'3t^2-2t^3; corresponding 33 profile vertices; thickness .1->.6'},
            {'progress_m':[240,1690],'profile':'DefaultArc'},
            {'progress_m':[1690,1720],'profile':'Blend','from':'DefaultArc','to':'SlimUpright','blend':'3t^2-2t^3; corresponding 33 profile vertices; thickness .6->.1'},
            {'progress_m':[1720,length],'profile':'SlimUpright'}],
        'generation_sampling':'Insert every boundary and intermediate rows at <=.5 m centreline progress, retain the exact road edge interpolation and rawProgress at portal splits and seam merges. No transition inside a skipped tube/jump run.',
        'validation_envelope':'Slim B: rectangular 0..6 high x 0..0.1 wide; blend B: 0..6 x 0..(11.01*(1-a)+.1*a), where a is minimum Slim weight over the whole segment, enclosing every blended 33-vertex cross-section at every height. Native segment joins plus <=.5 m subdivisions.',
        'readme':'barrier-code-plan.md'}

def export():
    source_paths=list(prev.load_json('verification-plan2.json')['source_sha256'])
    source_paths+=['Assets/Editor/CourseBarrierValidation.cs','Assets/Editor/RaceCourseValidation.cs','Assets/Editor/RaceBarrierPathValidation.cs']
    hashes={n:hashlib.sha256((ROOT/n).read_bytes()).hexdigest() for n in source_paths}
    print('Checking final Plan B profiles and complete school polygons...',flush=True)
    p,m=candidate();ss,g,hits,tested=check(p,m,('A','B_full','C','WallEnvelope'),new_profile=True)
    summary={n:{z:{'intervals_m':prev.runs(ids,ss),'segment_count':len(ids)} for z,ids in row.items()} for n,row in hits.items()}
    base=gc.course_points();base_g=predict.build(base);fine=predict.build(p,.25)
    metrics={};acceptance={}
    for name,data in [('normal_1_5m',g),('fine_0_25m',fine)]:
        metrics[name]=predict.metrics(data)
        acceptance[name]={'length_change_percent':100*(data['progress'][-1]/base_g['progress'][-1]-1),
            'aligned_before_takeoff_m':predict.aligned_run(data,(584,18,598),1),
            'aligned_after_landing_m':predict.aligned_run(data,(596,16,598),-1),
            'road_mesh':predict.road_mesh_metrics(data)}
    meshes=gc.load_json('full-meshes.json');moved=[(n,o) for n,o in meshes.items() if gc.move_for(n,m)!=(0,0)]
    separations=[];ground_rows=[];ground=meshes[next(n for n in meshes if n.endswith('/Plane'))]
    for n,o in moved:
        dx,dz=gc.move_for(n,m)
        lo=[o['min'][0]+dx,o['min'][1],o['min'][2]+dz];hi=[o['max'][0]+dx,o['max'][1],o['max'][2]+dz]
        for b,v in meshes.items():
            if tuple(gc.move_for(b,m))==(dx,dz) or b.endswith('/Plane'):continue
            distance=math.sqrt(sum(max(0,v['min'][k]-hi[k],lo[k]-v['max'][k])**2 for k in range(3)))
            separations.append((distance,n,b))
        supports=[]
        for tx,tz in [(0,0),(dx,dz)]:
            ll=[o['min'][0]+tx,o['min'][1],o['min'][2]+tz];hh=[o['max'][0]+tx,o['max'][1],o['max'][2]+tz]
            face=next((poly for poly in ground['polygons'] if prev.rectangle_on_face(ll,hh,poly)),None)
            assert face is not None,'Ground support polygon missing'
            norm=predict.cross(predict.sub(face[1],face[0]),predict.sub(face[2],face[0]))
            ys=[face[0][1]-(norm[0]*(x-face[0][0])+norm[2]*(z-face[0][2]))/norm[1] for x in (ll[0],hh[0]) for z in (ll[2],hh[2])]
            supports.append({'height_range_m':[min(ys),max(ys)],'filled_polygon_contains_footprint':True})
        delta=max(abs(b-a) for a,b in zip(supports[0]['height_range_m'],supports[1]['height_range_m']))
        ground_rows.append({'renderer':n,'old':supports[0],'new':supports[1],'height_delta_m':delta})
    closest=min(separations)
    counts={z:sum(z in row for row in hits.values()) for z in ('A','B_full','C','WallEnvelope')}
    tube_unchanged=[v for v in base if v['fit']]==[v for v in p if v['fit']]
    jump_unchanged=all(any(v==old for v in p) for old in base if tuple(old['p']) in ((584,18,598),(596,16,598)))
    course_ok=all(mm['heading_40m_degrees']<=6 and mm['min_radius']>=30 and mm['max_width_rate']<=.2 and mm['min_width']>=12 for mm in metrics.values()) and all(v['aligned_before_takeoff_m']>=6 and abs(v['length_change_percent'])<=3 and v['road_mesh']['folded_triangles']==0 for v in acceptance.values()) and tube_unchanged and jump_unchanged
    # Same source distance comparison as prior verification, not equal lap progress.
    def fit_start(points):
        j=next(i for i,v in enumerate(points) if v['fit'])
        return sum(predict.norm(predict.sub(points[i+1]['p'],points[i]['p'])) for i in range(j))
    oldfine=predict.build(base,.25);tube_deltas=[]
    for raw,v,owner in zip(fine['source_distance'],fine['path'],fine['owners']):
        if not p[owner]['fit']:continue
        target=raw-fit_start(p)+fit_start(base)
        j=min(len(oldfine['path'])-2,max(0,bisect.bisect_right(oldfine['source_distance'],target)-1))
        t=(target-oldfine['source_distance'][j])/(oldfine['source_distance'][j+1]-oldfine['source_distance'][j])
        tube_deltas.append(predict.norm(predict.sub(v,predict.lerp(oldfine['path'][j],oldfine['path'][j+1],t))))
    def at_z(z):
        result=[]
        for i,(a,b) in enumerate(zip(g['path'],g['path'][1:])):
            if min(a[2],b[2])<=z<=max(a[2],b[2]) and min(a[0],b[0])>490 and max(a[0],b[0])<500 and abs(b[2]-a[2])>1e-7:
                t=(z-a[2])/(b[2]-a[2]);c=predict.lerp(a,b,t);w=g['width'][i]+t*(g['width'][i+1]-g['width'][i]);s=g['progress'][i]+t*(g['progress'][i+1]-g['progress'][i])
                tangent=predict.sub(b,a);factor=predict.norm((tangent[0],0,tangent[2]))/abs(tangent[2]);envelope=(w/2+.2)*factor
                result.append({'world_z_m':z,'center_xyz_m':c,'raw_progress_m':s,'road_width_m':w,'required_opening_with_solid_m':2*envelope,'west_solid_gap_m':c[0]-envelope-484.5568,'east_solid_gap_m':505.5568-c[0]-envelope,'opening_m':21})
        return result
    gate=at_z(751.9)
    report={'status':'offline_geometry_verified_pending_physics_and_external_scene_clearance','zero_intrusion_target_met_for_school':counts['A']==counts['B_full']==0,
        'counts_objects':counts,'intrusions':summary,'course_metrics':metrics,'course_acceptance':acceptance,'course_hard_conditions_pass':course_ok,
        'home_minimum_width_m':min(v['w'] for v in p[-3:]),'north_start_authored_widths_m':[p[0]['w'],p[1]['w']],
        'tube_authored_points_widths_flags_unchanged':tube_unchanged,'jump_endpoints_widths_flags_unchanged':jump_unchanged,
        'tube_centerline_resampling_max_delta_m':max(tube_deltas),'preserved_tube_lead_in_m':predict.norm(predict.sub(p[4]['p'],p[3]['p'])),
        'moves':m,'moved_group_count':len(m),'moved_renderer_count':len(moved),'trees_moved':0,'maximum_displacement_m':5,
        'object_separation_lower_bound_m':closest[0],'closest_objects':closest[1:],'separation_scope':'all 550 school FBX objects, excluding filled support ground and rigid within-group contacts; external scene objects not certified',
        'ground':{'objects_y_displacement_m':0,'maximum_support_height_delta_m':max(r['height_delta_m'] for r in ground_rows),'tolerance_m':.001,'renderers':ground_rows},
        'gate':gate,'barrier_geometry':{'slim_minimum_vertical_height_m':6,'slim_top_height_m':6.1,'slim_solid_top_height_m':6.2,'slim_inner_reach_m':.1,'slim_complete_solid_reach_m':.2,'transition_length_each_m':30,'minimum_transition_top_height_m':6.1,'blended_volume_conservative_reach_m':11.01},
        'method':{'school_source_objects':len(meshes),'face_segment_candidates':tested,'segment_subdivision_m':.5,'native_joins_included':True,'guard':'5 mm plus road miter enclosure, scaled by 1+reach/halfWidth for profile normal sweep; faces clipped as polygons, not vertices only','floor_source':'existing actual cut OBJ for Cube.001/Plane.009/Plane.012; other objects full FBX; trees full source hull exclusion then conservative 0.5m world-height slabs','excluded':['support ground Plane','tube mesh and fitted road/jump gaps as in the source audit'],'profile_verification':'new B explicitly clipped; blend rectangular reach 11.01*(1-a)+.1*a at minimum Slim weight a on whole segment; WallEnvelope encloses complete changed solid profile, height 13.2*(1-a)+6.2*a and reach 11.61*(1-a)+.2*a; unchanged default upper geometry not re-certified'},
        'source_sha256':hashes,'predecessor_source_hashes_match':all(hashes[n]==h for n,h in prev.load_json('verification-plan2.json')['source_sha256'].items()),
        'unresolved':['Non-school scene props and runtime procedural scenery are not in the FBX/OBJ inventory; 0.3m clearance to these is not certified.','No Unity/physics/driving run: 6m wall height does not by itself prove containment; existing impact and wall-driving checks require profile-specific additions.','0.25m centreline comparison is an offline resampling check; continuous tube construction remains unchanged via the preserved straight lead-in.']}
    report['input_files_unchanged_during_verification']=all(hashlib.sha256((ROOT/n).read_bytes()).hexdigest()==h for n,h in hashes.items())
    write('verification.json',report);write('moves.json',m)
    write('course.json',{'status':report['status'],'source':'Assets/Editor/SchoolMapCourseSetup.cs','coordinate_space':'world XYZ metres; full road width','all_points_included':True,'tube_span_original_indices':[2,122],'tube_span_new_indices':[4,124],
        'waypoints':[{'index':i,'xyz':v['p'],'width':v['w'],'jumpToNext':v['jump'],'preserveAlignment':v['fit']} for i,v in enumerate(p)],
        'authored_table':[{'index':i,'xyz':v['p'],'width':v['w'],'jumpToNext':v['jump']} for i,v in enumerate(p) if not v['fit']],
        'home_width_policy':'18m minimum; 20m at north/start; retain original widths elsewhere except original bounded west lead-in and post-landing taper control'} )
    write('barrier-sections.json',sections(g['progress'][-1]));write('remaining-C.json',{n:r['C'] for n,r in summary.items() if 'C' in r})
    # Exact plane/face intersections used by the gate figure, not hand-drawn
    # pillar boxes. Source support geometry and the chosen gate are immutable.
    slice_z=751.9;lines=[]
    for n,o in meshes.items():
        if '/tree' in n or not (o['min'][2]<=slice_z<=o['max'][2]) or o['max'][0]<475 or o['min'][0]>515:continue
        for poly in o['polygons']:
            pts=[]
            for a,b in zip(poly,poly[1:]+poly[:1]):
                if (a[2]<=slice_z<b[2]) or (b[2]<=slice_z<a[2]):
                    t=(slice_z-a[2])/(b[2]-a[2]);pts.append([a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])])
            if len(pts)>=2:lines.append({'object':n,'points':pts})
    write('gate-slice.json',{'z':slice_z,'lines':lines})
    # Compact plot source, consumed by render.ps1 without any other writes.
    faces=[];trees=[]
    for n,o in meshes.items():
        if n.endswith('/Plane') or n.endswith('/BézierCurve.001'):continue
        dx,dz=gc.move_for(n,m)
        if '/tree' in n:trees.append([[x+dx,z+dz] for x,z in o['hull']]);continue
        for poly in o['polygons']:
            low=gc.clip(poly,lambda v:v[1]-3);low=gc.clip(low,lambda v:9-v[1])
            if low:faces.append({'xz':[[v[0]+dx,v[2]+dz] for v in low],'kind':0})
            elif min(v[1] for v in poly)>=9 and gc.area2([v[::2] for v in poly])>30:faces.append({'xz':[[v[0]+dx,v[2]+dz] for v in poly],'kind':1})
    def quads(data):
        l,r=predict.road_edges(data)
        return [[l[i][::2],l[i+1][::2],r[i+1][::2],r[i][::2]] for i in range(len(l)-1) if not data['points'][data['owners'][i]]['jump']]
    left,right=predict.road_edges(g)
    plot={'faces':faces,'trees':trees,'old':quads(base_g),'new':quads(g),'moved_hulls':[[[x,z+5] for x,z in o['hull']] for n,o in moved],
        'slim_edges':[[edge[i][::2],edge[i+1][::2]] for edge in [left,right] for i in range(len(edge)-1) if profile_kind(g['progress'][i])=='SlimUpright'],
        'blend_edges':[[edge[i][::2],edge[i+1][::2]] for edge in [left,right] for i in range(len(edge)-1) if profile_kind(g['progress'][i])=='Blend'],
        'gate':gate,'counts':counts}
    (HERE/'plot.json.gz').write_bytes(gzip.compress(json.dumps(plot,separators=(',',':')).encode('utf-8'),mtime=0))
    print(json.dumps({k:report[k] for k in ['status','counts_objects','course_hard_conditions_pass','object_separation_lower_bound_m','gate']},ensure_ascii=False),flush=True)
    return report

if __name__=='__main__':
    if '--export' in sys.argv:
        result=export()
        sys.exit(0 if result['zero_intrusion_target_met_for_school'] and result['course_hard_conditions_pass'] and result['counts_objects']['WallEnvelope']==0 and result['input_files_unchanged_during_verification'] else 2)
    else:main()
