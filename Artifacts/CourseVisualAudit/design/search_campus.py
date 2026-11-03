from geometry_check import *

STRUCTURE=['Cube','Cube.001','Cube.014','Cube.015','Cube.016','Cube.035','Cube.047','Plane.009','ステージ台:1/R3-C2','立方体.002','立方体.003','高校棟昇降口','高校棟昇降口.008','高校棟昇降口.009','高校棟昇降口.016','高校棟昇降口.045']

def campus(plan):
    p=course_points();z=815 if plan==1 else 805
    p[0]['p']=(520,3,z);p[1]['p']=(258.2,3,z)
    p[-1]['p']=(520,3,720);p[-2]['p']=(520,3,650)
    moves={'1016こうしゃ/'+s:(0,-180 if plan==1 else -160) for s in STRUCTURE}
    moves.update({'1016こうしゃ/Cube.008':(240,0),'1016こうしゃ/Cylinder.002':(50,0),'1016こうしゃ/Cube.006':(0,-129),'1016こうしゃ/Cube.013':(-100,0)})
    for n in load_json('full-meshes.json'):
        if n.split('/')[1].startswith('人間'):moves[n]=(0,30)
    return p,moves

if __name__=='__main__':
    for plan in (1,2):
        p,m=campus(plan)
        result=check(p,m,top=6 if plan==1 else 3,full=True,quick=False,include=lambda n:not n.startswith('1016こうしゃ/BézierCurve.001') and n!='1016こうしゃ/Plane')
        write(f'campus-check{plan}.json',result);write(f'campus-points{plan}.json',p);write(f'campus-moves{plan}.json',m)
        print(plan,json.dumps(result,ensure_ascii=False),flush=True)
        print('tube heights',min(v['p'][1] for v in p if v['fit']),max(v['p'][1] for v in p if v['fit']))
