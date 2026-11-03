from geometry_check import *

def snake(topx=495,bendx=450,bendz=750,body=-12,low=False):
    p=course_points();p[0].update(p=(topx,3,803.5),w=12);p[1].update(p=(258.2,3,803.5),w=12)
    p[-5]['w']=14.2;p[-4]['w']=13;p[-3].update(p=(489,3,619),w=12)
    p[-2].update(p=(489,3,650),w=12);p[-1].update(p=(489,3,695),w=12)
    p.append(dict(p=(bendx,3,bendz),w=12,jump=False,fit=False))
    moves={'1016こうしゃ/Cube.008':(0,16),'1016こうしゃ/Cube.026':(body,0)}
    trees=[n for n in load_json('full-meshes.json') if n.startswith('1016こうしゃ/Cube.026/tree')]
    for n in trees:moves[n]=(0,0)
    for n in load_json('full-meshes.json'):
        if n.split('/')[1].startswith('人間'):moves[n]=(0,10)
    for name,x in [('tree.004',560),('tree.005',600),('tree.016',640),('tree.018',680)]:
        n='1016こうしゃ/Cube.026/'+name;s=load_json('full-meshes.json')[n]
        moves[n]=(x-(s['min'][0]+s['max'][0])/2,790-(s['min'][2]+s['max'][2])/2)
    return p,moves

if __name__=='__main__':
    results=[]
    for tx in [480,495,510]:
        for bx in [440,450,460]:
            for bz in [745,755]:
                p,m=snake(tx,bx,bz)
                r=check(p,m,include=lambda n:n.startswith('1016こうしゃ/Cube.047/Plane.019') or n.startswith('1016こうしゃ/Cube.026'),quick=True)
                counts={n:{z:v['sample_segments'] for z,v in row.items()} for n,row in r['hits'].items()}
                print(tx,bx,bz,counts,flush=True)
                results.append({'topx':tx,'bendx':bx,'bendz':bz,'hits':r['hits'],'metrics':r['course_metrics'],'points':p,'moves':m})
                write('snake-results.json',results)
