"""Format the verified candidate's outcome without promoting it to a pass."""
from geometry_check import *

def fmt_runs(rows):return '; '.join(f'{a:.3f}–{b:.3f}' for a,b in rows)

def main():
    r=load_json('verification-plan2.json');b=load_json('barrier-plan2.json')
    r['object_separation_scope']='All other objects in the 550-object school FBX inventory. Non-school scene props are not contained in this mesh input and are not certified.'
    r['unresolved'].append('Separation from non-school scene props is not certified by the supplied school-model inventory.')
    r['unresolved']=list(dict.fromkeys(r['unresolved']))
    flare3=next(k*3+v for low,high,k,v in intervals(3) if low<=3<=high)
    r['bottleneck']['faceted_barrier_reach_at_3_m']=flare3
    r['bottleneck']['required_for_width_12_faceted_B_at_3_m']=12+2*flare3
    write('verification-plan2.json',r)
    b['reason']='Height 1.1 gives a full return profile approximately 2.31 m high. This shrinks B below 3 m, so it is not an upper-only remedy. Do not apply without a revised requirement.'
    write('barrier-plan2.json',b)
    metrics=r['course_metrics'];a=metrics['normal_1_5m'];f=metrics['fine_0_25m'];acc=r['course_acceptance'];counts=r['counts_objects'];ground=r['ground']
    crows='\n'.join(f'| `{n}` | {fmt_runs(v["C"]["intervals_m"])} |' for n,v in r['intrusions'].items() if 'C' in v)
    brows='\n'.join(f'| `{n}` | {fmt_runs(v["B_low"]["intervals_m"])} |' for n,v in r['intrusions'].items() if 'B_low' in v)
    profiles='\n'.join(f'| {fmt_runs([v["progress_m"]])} | {v["outwardFlare"]} | {v["wallHeight"]} | 条件外の参考値・適用保留 |' for v in b['intervals'])
    text=f'''# 制限内の案2候補と未達条件

**要求をすべて満たす案2は確定できていない。ゲームへの適用は推奨しない。**
移動後の保守的検証は **A={counts['A']}物体、既定の B(0〜3m)={counts['B_low']}物体**。
コースの必須数値条件は通常・0.25m細分化の両方で合格した。
ただし B の低い帯が残るため、成果物の状態は `partial_not_accepted`。
これは完成案ではなく、制限内で改善できる範囲と、現在の経路で解けない開口の記録である。

元指示書 `C:/Users/tsuka/.claude/codex-lead/jobs/20261021-1718-Racing-playability-audit/D1/prompt.md` と引き継ぎの移動制限を読んで再設計した。
160〜240mの建物移動を含む既存 `campus-*` / `search-*` の候補は採用していない。
ゲームファイル、Unity、git の commit / push / ブランチには操作していない。

## 比較と推奨

| 項目 | 案1・完全 | 案2・低い帯の候補 |
|---|---|---|
| 判定 | 現在の校舎下の出口を保持する条件では不可 | 未達・適用保留 |
| 移動単位 / Renderer数 | 採用できる移動案なし | 北側棟1グループ / {r['moved_renderer_count']} |
| 最大移動 | — | XZ 10m、Y 0m |
| 木 | 採用案なし | 移動0本、列の向き・間隔は不変 |
| A / 既定B(0〜3m) | 完全ゼロは証明できず | {counts['A']} / {counts['B_low']}物体 |
| 既定B(0〜6m) | 必要な開口幅を確保できず | {counts['B_full']}物体（上部を含む保守的候補） |
| コース | 全体を満たす新表は提示できず | 約{a['length']:.3f}m、+{acc['normal_1_5m']['length_change_percent']:.3f}%。北側・ホーム幅12m |
| バリア | 既定断面のままでは通過不可 | 区間値をJSONに記録。ただし低い帯まで縮めるため条件外 |
| 残る懸念 | 全経路の不可能証明ではない | 低い帯1物体、20mホーム優先は未達、学校外の小物との隙間は未検証 |

**推奨判断:** 案2を検討の主案として残すが、現段階ではどちらも採用しない。
低い帯まで縮める1.1mバリアを「上部だけの抑制」と読み替えて合格扱いにしない。
全世界の迂回路が不可能という結論も出していない。
別経路を探す設計作業と、低い帯の要件を維持したまま通せるかの判断が未解決である。

## 現在の経路のボトルネック

`Cube.047/Plane.019` という**同一メッシュ**に含まれる北端の柱で、
西柱の右面 X=484.5568 と東柱の左面 X=505.5568 の間は **21m**。
Zはおよそ749.914〜753.814m。剛体グループの平行移動はこの21mを広げない。

既定断面は `RaceBarrierPath.GetArcRadius` の `max(2*wallHeight, outwardFlare/sin60°)` に従う。
`minimumWallHeight=6` のため半径12mとなり、
高さ3mでの張り出しは連続円弧で `sqrt(2*12*3 - 3²) = 7.937254m`。
幅12mに必要な開口は **27.874508m**、不足は **6.874508m**。
実装の16分割断面では張り出しは {flare3:.6f}m、必要幅は **{12+2*flare3:.6f}m** で、この21mに収まらない。
案1の高さ6mでは `12 + 2*12*sin60° = 32.784610m` が必要。
斜めに通る場合も、開口面上で必要な幅は小さくならない。

これは**保持した校舎下の出口に対する局所的な必要幅の不成立**である。
同じ柱メッシュの部品だけを離すにはメッシュ編集が必要となり、許可範囲外。
天井が約Y=12mにあるため、単純に道路を持ち上げて柱を越えるとAの高さ6m条件とも衝突する。
校舎西側への迂回も探索したが、元OBJに入っていない柱を全FBX面で調べると侵入が残ったため採用しなかった。

## 移動と自然さ

`moves-plan2.json` はワールドの `[dx, dz]`。基準配置からの絶対変位であり、相対加算を繰り返さない。

| グループ | dx | dz | 理由 |
|---|---:|---:|---|
| `1016こうしゃ/Cube.008` と全子 | 0m | +10m | 北側棟の壁・屋根・窓・柱を一緒にずらし、北側路線の余地を増やす |

この6 Rendererは `Cube.008`、`Cube.009`、`Cube.010`、`Plane.001`、`Plane.002`、`Plane.003`。
親Transformに一度だけ変位を与える。内部の相対変位0m、回転・スケール・Yは不変。
他の校舎棟は動かさず、中庭と各棟の並びを保持する。北側棟との通路方向の間隔だけ10m広がる。
木を動かさないので、並木の間隔・向き・木と建物の元の関係は維持される。

移動グループと他の校舎モデル物体との3D AABB距離の下限は **{r['object_separation_lower_bound_m']:.6f}m ≥ 0.3m**。
最接近の下限を与える組は `{r['closest_objects'][0]}` / `{r['closest_objects'][1]}`。
AABBの重なりを隙間ゼロと誤判定せず、内部で接している同じ建物の壁・屋根は同じ移動単位として扱った。
対象は学校FBXの550物体。学校外の灯籠・ゲートなどはこの入力に含まれないため、全シーンに対する隙間合格は宣言しない。

## 地面

元位置と移動先の各RendererのXZ外接矩形が、`1016こうしゃ/Plane` の**穴を除く実ポリゴン内**に収まることを確認した。
凹形ポリゴンに対して、矩形4隅の内包と境界が矩形を横切らないことを確認している。
全体のPlaneのAABBだけで地面があるとは判断していない。
支持面の微小なFBX傾きを含む高さ差の最大値は **{ground['old_to_new_ground_height_difference_max_m']:.10f}m**。
1mm許容値以下。物体のY変位は0m。新旧の支持面高さ範囲はJSONに記録した。

## コース表と合格条件

`course-plan2.json` の `authored_table` が `SchoolMapCourseSetup` の新しい手入力点・全幅の表。
`waypoints` にはチューブを含む全{len(load_json('course-plan2.json')['waypoints'])}点とフラグを収録した。
追加点のためindexが変わるので、元のindexだけを使って適用しない。

- 北東点を (494, 6, 800)、北西点を (245, 1, 800) にし、北側を幅12mにする。
- 西側接近路に (260, 3, 640) と、元の直線上のZ=595点を追加。チューブ直前の約38mは元の方向・高さを保つ。
- 着地後の直線に (607.5, 12, 598)、幅17.93mの幅制御点を追加。元の幾何線上で、踏切・着地は変えない。
- 入口脇の幅14m、ホーム導入以後は12m。ホーム補助点のXを489.5 / 494に調整する。
- 踏切前2点のZを599.53816 / 600.42658に微調整して、40mの方位変化を6°以内に収める。
- ホーム20mは優先希望として認識しているが、この候補では保持できない。

| 指標 | 通常1.5m | 細分化0.25m | 条件 |
|---|---:|---:|---|
| 全長 m | {a['length']:.6f} | {f['length']:.6f} | 元 {r['baseline_length_m']:.6f}m の±3% |
| 全長変化 % | {acc['normal_1_5m']['length_change_percent']:.6f} | {acc['fine_0_25m']['length_change_percent']:.6f} | ±3 |
| 踏切前40mの方位変化 ° | {a['heading_40m_degrees']:.6f} | {f['heading_40m_degrees']:.6f} | ≤6 |
| 接近路の最小半径 m | {a['min_radius']:.6f} | {f['min_radius']:.6f} | ≥30 |
| 幅変化率 m/m | {a['max_width_rate']:.6f} | {f['max_width_rate']:.6f} | ≤0.2 |
| 最小幅 m | {a['min_width']:.3f} | {f['min_width']:.3f} | ≥12 |
| 踏切軸±0.5°の整列長 m | {acc['normal_1_5m']['axis_aligned_before_takeoff_m']:.6f} | {acc['fine_0_25m']['axis_aligned_before_takeoff_m']:.6f} | ≥6 |
| 道路面の折り返し三角形 | {acc['normal_1_5m']['road_mesh']['folded_triangles']} | {acc['fine_0_25m']['road_mesh']['folded_triangles']} | 0 |

5m窓による接近路半径は {r['approach_5m_window_min_radius_m']:.6f}m。
踏切 (584,18,598)、着地 (596,16,598)、それぞれの幅・jumpフラグと、チューブ121点・幅・フラグは不変。
チューブの連続線形を変えない直前リードインは {r['tube_original_straight_lead_in_preserved_m']:.6f}m。
同じsource距離にそろえた0.25mサンプル比較の最大差は {r['tube_centerline_resampling_max_delta_m']:.9f}m（再サンプル・補間差）。

## 侵入の検証と残るC

コースの元サンプル接続をすべて含み、最大0.5mで細分化した区間に対して面をクリップした。
非木は全FBXポリゴンを使用する。既存の3床だけは、すでに切り抜かれた実OBJ面を使用する。
木は全FBX bounds / 凸包を先に調べ、必要なものだけOBJの高さ0.5m別の保守的凸包で確認する。
古いOBJに出力された物体・面だけで、移動後の新経路を合格にすることは避けた。
チューブメッシュとチューブ内区間、道路より下の支持地面は元監査と同じく対象外。
5mmと道路のmiter外接誤差を加え、頂点だけでなく面の横断を検出する。

**Aは0、既定B(0〜3m)は{counts['B_low']}物体で未達。** 残る区間は次のとおり。
区間は新コースのraw progress [m]で、進行方向と逆。旧コースのprogressとは一致しない。

| 物体 | B(0〜3m)が残るprogress m |
|---|---|
{brows}

残るCは次の{counts['C']}物体。校舎の下を通る意図に沿って残す。Cの除去は主案の目標にしていない。

| 物体 | Cのprogress m |
|---|---|
{crows}

全B(0〜6m)の{counts['B_full']}物体と全区間は `verification-plan2.json`、Cのみは `remaining-C-plan2.json`。
細分化した区間の境界による微小な区間分割はJSONの値をそのまま残した。

## バリア値の保留案

`barrier-plan2.json` に、既定Bの残る範囲を12m外へ広げた区間と次の値を記録した。
**これは条件を満たす主案の値ではない。適用保留の参考案である。**

| raw progress m | outwardFlare m | wallHeight m | 状態 |
|---|---:|---:|---|
{profiles}

`outwardFlare` を0にするだけでは `minimumWallHeight=6` により半径12mのまま。
`wallHeight=1.1` を有効にするには、このパスの `minimumWallHeight` も0.1などに下げる必要がある。
この値では、上部の戻り円弧を含む壁高自体が約2.31mまで下がり、**0〜3m帯の保護範囲も縮む**。
3〜6mの侵入だけを取り除く変更ではないため、要求の達成として数えない。
生成側の `RaceCourseBarriers.Create → SetWorldPath` は制御点を既定値で再作成するので、
将来適用する場合は生成中のprogressに対応して断面を設定する仕組みが必要。今回そのコードは変更していない。
参考断面の実バリア、レール、厚さ0.6mのColliderとの隙間や乗り越え防止は未検証。
滑らかな接続値も未確定で、JSONは区間と低減値までの記録に留めている。

## 再現と成果物

リポジトリのルートで次を実行する。インストール・Unity起動は不要。

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
$env:PYTHONIOENCODING='utf-8'
& 'C:/msys64/clangarm64/bin/python.exe' Artifacts/CourseVisualAudit/design/verify_plan2.py
& 'C:/msys64/clangarm64/bin/python.exe' Artifacts/CourseVisualAudit/design/write_readme.py
& Artifacts/CourseVisualAudit/design/render_plan2.ps1
```

検証スクリプトは低い帯の未達を成功として返さない。`zero_intrusion_target_met=false`。
実行中に入力ファイルのSHA-256が変わらないことも確認済み。ハッシュはJSONに収録。
`after-plan2.png` は移動後候補の俯瞰図で、赤が残る低い帯。図にも未採用候補と明記した。
`moves-plan1.json` の空オブジェクトは「実行可能な案1なし」のプレースホルダーで、ゼロ移動の合格案を意味しない。

残る作業は、元のB(0〜3m)を保持する別経路の確定、全シーン小物との隙間の確認、
ホーム20mとの両立の判断。採用可能な案が確定するまでは、ゲームへの反映作業に進まない。
'''
    (OUT/'README.md').write_text(text,encoding='utf-8')

if __name__=='__main__':main()
