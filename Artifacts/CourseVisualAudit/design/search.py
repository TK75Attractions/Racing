from geometry_check import *
import copy

if __name__=='__main__':
    results=[]
    for topx in [489,495,501,507,513]:
        for lastz in [735,750,765]:
            p=course_points();p[0].update(p=(topx,3,803.5),w=12);p[1].update(p=(258.2,3,803.5),w=12)
            p[-1].update(p=(489,3,lastz),w=12);p[-2].update(p=(489,3,650),w=12)
            moves={'1016こうしゃ/Cube.008':(0,16)}
            for n in json.loads((OUT/'full-meshes.json').read_text(encoding='utf-8')):
                if n.split('/')[1].startswith('人間'):moves[n]=(0,10)
            r=check(p,moves,include=lambda n:n.startswith('1016こうしゃ/Cube.026/tree.016') or n.startswith('1016こうしゃ/Cube.047/Plane.019') or n.startswith('1016こうしゃ/Cube.008'),quick=True)
            counts={n:{z:row['sample_segments'] for z,row in v.items()} for n,v in r['hits'].items()}
            print(topx,lastz,counts,flush=True)
            results.append({'topx':topx,'lastz':lastz,'hits':r['hits'],'metrics':r['course_metrics'],'points':p,'moves':moves})
            write('search-results.json',results)
