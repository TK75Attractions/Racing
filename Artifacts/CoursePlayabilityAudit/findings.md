# SampleScene コース走行性監査

Unity 6000.3.9f1; scene=Assets/Scenes/SampleScene.unity; コース全長=1908.335m; 丸め後中心線0.5m間隔=3817地点。シーン保存・Asset保存APIは使用しない。

スタートからの距離 = Repeat(direction * (progress - startProgress), 全長)。startProgress=1860.856m; direction=-1; reference=LapManager progress reference (goal)。逆方向では (startProgress-progress) mod 全長。

## 集計

- ジャンプ区間: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 見た目の侵入: 18件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 実走大減速: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 頭上Collider: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 頭上Renderer候補: 8件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）

## 計測方法と限界

左右境界はTryGetSampleAtProgressのleftEdge/rightEdge。距離は丸め後3D中心線の距離（スタートライン基準とは限らない）。各0.5m区間の断面にColliderの実三角形をクリップし、左右境界からの最大到達深さをmで記録。高さは路面中心線からワールド鉛直上方（区間勾配を補正）。車高帯は0.02〜2.5m（接地面の数値誤差除外）。外側0〜1.5mは別区分。頭上は2.5〜4m、0〜2.5mは侵入表を参照。BoxColliderは変換後の実OBB、MeshColliderはmesh三角形。convex mesh・その他Colliderは候補扱いで精度を明記する。巨大な閉じた形状が区間を完全に内包し表面が区間外の場合は未検出になり得る。

Rendererは有効Colliderを自身・祖先・子に持たない有効物体だけ。world boundsをクリップするため、空白を含む保守的な候補であり、実ポリゴン侵入とは断定しない。道路生成物・チューブ走行面・RaceBarrierPath・車を除外。シーンを開く際のExecuteAlwaysが生成した一時道路・バリアを含めて計測。無効物体とtriggerは対象外。

幅変化は5m窓で0.2m/m以上、曲率は5m窓で0.06rad/m以上を抽出。チューブ入口は既存の壁/天井ray判定の切替点±2mを0.1mで中心・左右35%位置計測。バリアは0.5m毎・高さ1mの側方rayと基部端点/隣接点の検査。表面全体の微細な継ぎ目や全高さの隙間を保証する検査ではない。

物理Collider総数=541; 道路/バリア/車の除外=5; 視覚候補Renderer=675. 生データはsamples.csv、全地点の幅・曲率・路面・バリアrayはgeometry.csv。hitsは検出回数で、first〜last全区間の連続占有を意味しない。

- 路面ray対象Collider=3（生成道路・チューブ、DontSaveを含むシーン階層から取得）
- Player1_Car[20]/TirePos_FR[2]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player1_Car[20]/TirePos_FL[3]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player1_Car[20]/TirePos_BR[4]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player1_Car[20]/TirePos_BL[5]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_FR[2]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_FL[3]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_BR[4]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_BL[5]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測

## 重要度順の検出一覧

| # | 重要度 | 種類 | オブジェクト（ヒエラルキー）| コース距離 / 座標 | スタートからの距離 | 車線（半幅比）| 計測値 | プレイへの影響 |
|---|---|---|---|---|---|---|---|---|
|1|中|実走大減速|Player1_Car[20] (Rigidbody)|1785.097m / (497.086, 3.348, 656.088)|75.758m|-0.75|14.78m/s; 車高帯=True; hits=2; 1固定stepで速度が35%以上減少; 26.785→12.006m/s|衝突・急制動などによる速度損失|
|2|中|頭上Collider|1016こうしゃ[12]/Cube.047[14]/Plane.019[16] (MeshCollider)|1771.5m / (496.374, 3.004, 642.562)|89.356m||2.999m; 車高帯=False; hits=8; 高さ2.5〜4m部分; 実形状|ジャンプ・車体上部または視界への影響候補|
|3|参考|ジャンプ区間|GameObject[4] (RaceCourse)|1660.188m / (584, 18, 598)|200.668m||12.166m; 車高帯=False; hits=1; 離陸=1648.023m; 着地=1660.188m|路面のない区間。長さは丸め後中心線の距離|
|4|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/LaserPink[3] (MeshRenderer)|1862m / (495.025, 3, 732.95)|1907.19m||20m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|5|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/MidnightMetal[0] (MeshRenderer)|1862m / (495.025, 3, 732.95)|1907.19m||20m; 車高帯=True; hits=7; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|6|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/GraphiteInset[1] (MeshRenderer)|1861.5m / (495.018, 3, 732.45)|1907.69m||20m; 車高帯=True; hits=5; Renderer world AABB（保守的な候補）; 高さ=0.725〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|7|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/IceWhite[4] (MeshRenderer)|1861.5m / (495.018, 3, 732.45)|1907.69m||20m; 車高帯=True; hits=5; Renderer world AABB（保守的な候補）; 高さ=1.063〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|8|参考|見た目の侵入|RaceItems[11]/Item Box Row 1[0]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|1822.5m / (494.71, 3, 693.452)|38.356m||11.026m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|9|参考|見た目の侵入|RaceItems[11]/Charge Crystal 1[1]/ItemPickupVisual[0] (MeshRenderer)|1756.5m / (501.491, 3.255, 628.474)|104.356m||10.6m; 車高帯=True; hits=4; Renderer world AABB（保守的な候補）; 高さ=0.4〜2.01m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|10|参考|見た目の侵入|RaceItems[11]/Charge Crystal 4[6]/ItemPickupVisual[0] (MeshRenderer)|251.5m / (261.672, 3.293, 761.585)|1609.356m||9.955m; 車高帯=True; hits=3; Renderer world AABB（保守的な候補）; 高さ=0.408〜2.012m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|11|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/IonCyan[2] (MeshRenderer)|1860m / (494.999, 3, 730.951)|0.856m||9.899m; 車高帯=True; hits=5; Renderer world AABB（保守的な候補）; 高さ=0.355〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|12|参考|見た目の侵入|RaceItems[11]/Item Box Row 2[3]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|1624m / (618.991, 9.249, 598.672)|236.856m||8.997m; 車高帯=True; hits=7; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.337m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|13|参考|見た目の侵入|RaceItems[11]/Item Box Row 3[5]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|373m / (273.627, 4.302, 640.678)|1487.856m||8.512m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|14|参考|見た目の侵入|RaceItems[11]/Charge Crystal 2[2]/ItemPickupVisual[0] (MeshRenderer)|1606.5m / (636.214, 6.962, 600.725)|254.356m||7.094m; 車高帯=True; hits=3; Renderer world AABB（保守的な候補）; 高さ=0.365〜2.027m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|15|参考|見た目の侵入|RaceItems[11]/Charge Crystal 3[4]/ItemPickupVisual[0] (MeshRenderer)|496m / (281.9, -2.404, 518.739)|1364.856m||6.484m; 車高帯=True; hits=3; Renderer world AABB（保守的な候補）; 高さ=0.247〜1.93m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|16|参考|見た目の侵入|RaceItems[11]/Item Box Row 1[0]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|1824m / (494.714, 3, 694.952)|36.856m||5.029m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|17|参考|見た目の侵入|RaceItems[11]/Item Box Row 1[0]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|1821.5m / (494.708, 3, 692.452)|39.356m||5.029m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|18|参考|見た目の侵入|RaceItems[11]/Item Box Row 2[3]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|1624m / (618.991, 9.249, 598.672)|236.856m||3.164m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.337m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|19|参考|見た目の侵入|RaceItems[11]/Item Box Row 2[3]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|1624.5m / (618.502, 9.341, 598.631)|236.356m||3.008m; 車高帯=True; hits=7; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.245m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|20|参考|見た目の侵入|RaceItems[11]/Item Box Row 3[5]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|372m / (273.528, 4.294, 641.673)|1488.856m||2.626m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.474m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|21|参考|見た目の侵入|RaceItems[11]/Item Box Row 3[5]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|374m / (273.725, 4.31, 639.683)|1486.856m||2.594m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.457m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|22|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/GraphiteInset[1] (MeshRenderer)|1860m / (494.999, 3, 730.951)|0.856m||2.5m; 車高帯=False; hits=5; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|23|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/IceWhite[4] (MeshRenderer)|1860m / (494.999, 3, 730.951)|0.856m||2.5m; 車高帯=False; hits=5; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|24|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/IonCyan[2] (MeshRenderer)|1860m / (494.999, 3, 730.951)|0.856m||2.5m; 車高帯=False; hits=5; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|25|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/LaserPink[3] (MeshRenderer)|1859.5m / (494.992, 3, 730.451)|1.356m||2.5m; 車高帯=False; hits=6; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|26|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/MidnightMetal[0] (MeshRenderer)|1859.5m / (494.992, 3, 730.451)|1.356m||2.5m; 車高帯=False; hits=7; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|27|参考|頭上Renderer候補|RaceItems[11]/Item Box Row 2[3]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|1621.5m / (621.447, 8.845, 598.905)|239.356m||2.5m; 車高帯=False; hits=4; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|28|参考|頭上Renderer候補|RaceItems[11]/Item Box Row 2[3]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|1621.5m / (621.447, 8.845, 598.905)|239.356m||2.5m; 車高帯=False; hits=4; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|29|参考|頭上Renderer候補|RaceItems[11]/Item Box Row 2[3]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|1621.5m / (621.447, 8.845, 598.905)|239.356m||2.5m; 車高帯=False; hits=4; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|

実走状態: 実走終了（車線別の完了状態を参照）

## 実走結果

実走終了（車線別の完了状態を参照）; simulation=99.215s; direction=-1. RoadObservationBotを実車へ入力。pose/速度の補正なし。各固定stepで接触ペアの発生を取得。

車線あたり制限=298.542s; スタート基準=LapManager progress reference (goal); progress=1860.856m。

|車線（半幅比）|車|状態|指定周回|実測周回相当|距離m|最高速m/s|障害物接触|バリア接触|コースアウト|大減速|停止回数|最大停止s|最大車線誤差m|目標±0.75m内の前進距離m|
|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
|-0.75|Player1_Car|完了|1|1|1908.889|49.943|0|72|0|2|0|0.06|12.865|1559.844|
|0|Player2_Car|完了|1|1|1908.917|49.946|0|0|0|0|0|0|2.207|1610.274|
|0.75|Player2_Car|完了|1|1|1908.592|55.693|0|9|0|0|0|0|4.224|1569.827|

制限: 車線はsample.right方向の半幅比。狭い区間では車体幅+端から1mの余白で制限。移行中もイベント集計し、目標車線への初回到達後の距離を周回換算。追従精度はdriving-trace.csvで確認。接触回数はColliderペアのEnter単位（接触点の行数とは異なる）。相対速度は前step車体の並進・角速度と相手の現在接触点速度による近似。全横位置の安全を保証しない。
- 路面ray対象Collider=3（生成道路・チューブ、DontSaveを含むシーン階層から取得）
- Player1_Car[20]/TirePos_FR[2]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player1_Car[20]/TirePos_FL[3]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player1_Car[20]/TirePos_BR[4]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player1_Car[20]/TirePos_BL[5]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_FR[2]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_FL[3]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_BR[4]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
- Player2_Car[23]/TirePos_BL[5]/Sport_Wheel_1[0]: SphereCollider はbounds候補としてのみ計測
