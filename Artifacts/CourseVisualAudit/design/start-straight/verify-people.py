"""Full-source model clearance and ground support for eight relocated people."""
from verify import *

meshes=gc.load_json('full-meshes.json')
names=['1016こうしゃ/人間'+('' if i==0 else '.%03d'%i) for i in range(8)]
moves={n:(0,20) for n in names}
moves['1016こうしゃ/Cube.008']=(0,23)
rows=[]
for name in names:
    model=meshes[name]
    lo=[model['min'][0],model['min'][1],model['min'][2]+20]
    hi=[model['max'][0],model['max'][1],model['max'][2]+20]
    separations=[]
    for other,value in meshes.items():
        if other==name or other=='1016こうしゃ/Plane':continue
        dx,dz=gc.move_for(other,moves)
        delta=[dx,0,dz]
        distance=math.sqrt(sum(max(0,value['min'][k]+delta[k]-hi[k],lo[k]-value['max'][k]-delta[k])**2 for k in range(3)))
        separations.append((distance,other))
    face=next((p for p in meshes['1016こうしゃ/Plane']['polygons'] if previous.rectangle_on_face(lo,hi,p)),None)
    rows.append(dict(name=name,north_translation_m=20,moved_bounds_m=[lo,hi],
                     footprint_on_original_filled_ground=face is not None,
                     closest_other_model_aabb_lower_bound_m=min(separations)[0],closest_other_model=min(separations)[1],
                     y_rotation_scale_modified=False,renderer_visibility_modified=False))
passed=all(r['footprint_on_original_filled_ground'] and r['closest_other_model_aabb_lower_bound_m']>=.3 for r in rows)
result=dict(status='accepted_offline' if passed else 'failed',people=rows,
            scope='All full-source school renderer bounds, including all other relocated people and the north wing at +23m. Excludes only the supporting Plane. AABB separation is a conservative lower bound. Filled source polygon support checks the entire XZ rectangle, not only corners.',
            limits='Source transforms and renderer enabled/active flags must also be checked in Unity. Course A/B exclusion must be rerun against the final adopted northeast connection; this check does not assume a pending geometry revision.')
write('people-verification.json',result)
print(json.dumps(dict(status=result['status'],minimum_other_model_clearance_m=min(r['closest_other_model_aabb_lower_bound_m'] for r in rows)),ensure_ascii=False))
sys.exit(0 if passed else 2)
