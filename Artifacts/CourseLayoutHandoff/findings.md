# SampleScene コース走行性監査

Unity 6000.3.9f1; scene=Assets/Scenes/SampleScene.unity; コース全長=1933.571m; 丸め後中心線0.5m間隔=3868地点。シーン保存・Asset保存APIは使用しない。

スタートからの距離 = Repeat(direction * (progress - startProgress), 全長)。startProgress=1861.162m; direction=-1; reference=LapManager progress reference (goal)。逆方向では (startProgress-progress) mod 全長。

## 集計

- ジャンプ区間: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 見た目の侵入: 18件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 実走衝突: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 実走大減速: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 頭上Collider: 1件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）
- 頭上Renderer候補: 8件（種類×Component単位。複数地点をまとめ最大侵入/最小離隔の地点を表示）

## 計測方法と限界

左右境界はTryGetSampleAtProgressのleftEdge/rightEdge。距離は丸め後3D中心線の距離（スタートライン基準とは限らない）。各0.5m区間の断面にColliderの実三角形をクリップし、左右境界からの最大到達深さをmで記録。高さは路面中心線からワールド鉛直上方（区間勾配を補正）。車高帯は0.02〜2.5m（接地面の数値誤差除外）。外側0〜1.5mは別区分。頭上は2.5〜4m、0〜2.5mは侵入表を参照。BoxColliderは変換後の実OBB、MeshColliderはmesh三角形。convex mesh・その他Colliderは候補扱いで精度を明記する。巨大な閉じた形状が区間を完全に内包し表面が区間外の場合は未検出になり得る。

Rendererは有効Colliderを自身・祖先・子に持たない有効物体だけ。world boundsをクリップするため、空白を含む保守的な候補であり、実ポリゴン侵入とは断定しない。道路生成物・チューブ走行面・RaceBarrierPath・車を除外。シーンを開く際のExecuteAlwaysが生成した一時道路・バリアを含めて計測。無効物体とtriggerは対象外。

幅変化は5m窓で0.2m/m以上、曲率は5m窓で0.06rad/m以上を抽出。チューブ入口は既存の壁/天井ray判定の切替点±2mを0.1mで中心・左右35%位置計測。バリアは0.5m毎・高さ1mの側方rayと基部端点/隣接点の検査。表面全体の微細な継ぎ目や全高さの隙間を保証する検査ではない。

物理Collider総数=541; 道路/バリア/車の除外=5; 視覚候補Renderer=683. 生データはsamples.csv、全地点の幅・曲率・路面・バリアrayはgeometry.csv。hitsは検出回数で、first〜last全区間の連続占有を意味しない。

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
|1|高|実走衝突|1016こうしゃ[12]/Plane.012[18]/Plane.017[1] (MeshCollider)|1618.931m / (626.088, 11.482, 587.066)|242.231m|-0.75|34.906m/s; 車高帯=True; hits=1; Player1_Car; t=13.82s; 接触点=(626.088, 11.482, 587.066); 法線=(-0.318, 0.582, 0.748); 相対速度は前step車体接触点速度との差|道路・バリア・車以外との実接触。衝突成立を確認|
|2|中|実走大減速|Player2_Car[23] (Rigidbody)|1647.634m / (596.782, 16.128, 597.849)|213.528m|0|16.389m/s; 車高帯=True; hits=2; 1固定stepで速度が35%以上減少; 20.093→3.704m/s|衝突・急制動などによる速度損失|
|3|中|頭上Collider|1016こうしゃ[12]/Cube.047[14]/Plane.019[16] (MeshCollider)|1772m / (496.324, 3.003, 642.748)|89.162m||2.999m; 車高帯=False; hits=8; 高さ2.5〜4m部分; 実形状|ジャンプ・車体上部または視界への影響候補|
|4|参考|ジャンプ区間|GameObject[4] (RaceCourse)|1660.496m / (584, 18, 598)|200.666m||12.166m; 車高帯=False; hits=1; 離陸=1648.33m; 着地=1660.496m|路面のない区間。長さは丸め後中心線の距離|
|5|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/GraphiteInset[1] (MeshRenderer)|1860.5m / (494.7, 3, 731.145)|0.662m||20m; 車高帯=True; hits=4; Renderer world AABB（保守的な候補）; 高さ=0.725〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|6|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/IceWhite[4] (MeshRenderer)|1860.5m / (494.7, 3, 731.145)|0.662m||20m; 車高帯=True; hits=4; Renderer world AABB（保守的な候補）; 高さ=1.063〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|7|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/LaserPink[3] (MeshRenderer)|1860m / (494.7, 3, 730.645)|1.162m||20m; 車高帯=True; hits=5; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|8|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/MidnightMetal[0] (MeshRenderer)|1860m / (494.7, 3, 730.645)|1.162m||20m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|9|参考|見た目の侵入|RaceItems[11]/Item Box Row 1[0]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|1821.5m / (494.7, 3, 692.145)|39.662m||11.025m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|10|参考|見た目の侵入|RaceItems[11]/Charge Crystal 1[1]/ItemPickupVisual[0] (MeshRenderer)|1757m / (501.423, 3.251, 628.654)|104.162m||10.674m; 車高帯=True; hits=3; Renderer world AABB（保守的な候補）; 高さ=0.404〜2.014m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|11|参考|見た目の侵入|RaceItems[11]/Charge Crystal 4[6]/ItemPickupVisual[0] (MeshRenderer)|252m / (261.691, 3.295, 761.394)|1609.162m||9.933m; 車高帯=True; hits=3; Renderer world AABB（保守的な候補）; 高さ=0.406〜2.01m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|12|参考|見た目の侵入|Goal[5]/CircuitFinishGate[0]/IonCyan[2] (MeshRenderer)|1860m / (494.7, 3, 730.645)|1.162m||9.875m; 車高帯=True; hits=5; Renderer world AABB（保守的な候補）; 高さ=0.355〜2.5m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|13|参考|見た目の侵入|RaceItems[11]/Item Box Row 2[3]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|1624.5m / (618.803, 9.285, 598.656)|236.662m||9m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.301m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|14|参考|見た目の侵入|RaceItems[11]/Item Box Row 3[5]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|373m / (273.596, 4.299, 640.986)|1488.162m||8.522m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.468m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|15|参考|見た目の侵入|RaceItems[11]/Charge Crystal 2[2]/ItemPickupVisual[0] (MeshRenderer)|1607m / (636.024, 6.986, 600.702)|254.162m||7.087m; 車高帯=True; hits=4; Renderer world AABB（保守的な候補）; 高さ=0.341〜2.003m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|16|参考|見た目の侵入|RaceItems[11]/Charge Crystal 3[4]/ItemPickupVisual[0] (MeshRenderer)|496.5m / (281.9, -2.458, 518.555)|1364.662m||6.484m; 車高帯=True; hits=3; Renderer world AABB（保守的な候補）; 高さ=0.246〜1.983m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|17|参考|見た目の侵入|RaceItems[11]/Item Box Row 1[0]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|1821.5m / (494.7, 3, 692.145)|39.662m||5.025m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|18|参考|見た目の侵入|RaceItems[11]/Item Box Row 1[0]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|1821.5m / (494.7, 3, 692.145)|39.662m||5.025m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.465m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|19|参考|見た目の侵入|RaceItems[11]/Item Box Row 2[3]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|1624.5m / (618.803, 9.285, 598.656)|236.662m||3.183m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.301m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|20|参考|見た目の侵入|RaceItems[11]/Item Box Row 2[3]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|1624.5m / (618.803, 9.285, 598.656)|236.662m||3.005m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.301m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|21|参考|見た目の侵入|RaceItems[11]/Item Box Row 3[5]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|372m / (273.498, 4.291, 641.981)|1489.162m||2.631m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.475m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|22|参考|見た目の侵入|RaceItems[11]/Item Box Row 3[5]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|374m / (273.695, 4.308, 639.99)|1487.162m||2.599m; 車高帯=True; hits=6; Renderer world AABB（保守的な候補）; 高さ=0.02〜2.46m|視界・走路境界の誤認候補。実メッシュの占有は未確定|
|23|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/GraphiteInset[1] (MeshRenderer)|1860.5m / (494.7, 3, 731.145)|0.662m||2.5m; 車高帯=False; hits=4; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|24|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/IceWhite[4] (MeshRenderer)|1860.5m / (494.7, 3, 731.145)|0.662m||2.5m; 車高帯=False; hits=4; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|25|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/IonCyan[2] (MeshRenderer)|1860m / (494.7, 3, 730.645)|1.162m||2.5m; 車高帯=False; hits=5; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|26|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/LaserPink[3] (MeshRenderer)|1860m / (494.7, 3, 730.645)|1.162m||2.5m; 車高帯=False; hits=5; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|27|参考|頭上Renderer候補|Goal[5]/CircuitFinishGate[0]/MidnightMetal[0] (MeshRenderer)|1860m / (494.7, 3, 730.645)|1.162m||2.5m; 車高帯=False; hits=6; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|28|参考|頭上Renderer候補|RaceItems[11]/Item Box Row 2[3]/Item Box 1[0]/ItemPickupVisual[0] (MeshRenderer)|1622m / (621.258, 8.872, 598.885)|239.162m||2.5m; 車高帯=False; hits=3; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|29|参考|頭上Renderer候補|RaceItems[11]/Item Box Row 2[3]/Item Box 2[1]/ItemPickupVisual[0] (MeshRenderer)|1622m / (621.258, 8.872, 598.885)|239.162m||2.5m; 車高帯=False; hits=3; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|
|30|参考|頭上Renderer候補|RaceItems[11]/Item Box Row 2[3]/Item Box 3[2]/ItemPickupVisual[0] (MeshRenderer)|1622m / (621.258, 8.872, 598.885)|239.162m||2.5m; 車高帯=False; hits=3; 高さ2.5〜4m部分; Renderer AABB|ジャンプ・車体上部または視界への影響候補|

実走状態: 実走終了（車線別の完了状態を参照）

## 実走結果

実走終了（車線別の完了状態を参照）; simulation=191.644s; direction=-1. RoadObservationBotを実車へ入力。pose/速度の補正なし。各固定stepで接触ペアの発生を取得。

車線あたり制限=603.393s; スタート基準=LapManager progress reference (goal); progress=1861.162m。

|車線（半幅比）|車|状態|指定周回|実測周回相当|距離m|最高速m/s|障害物接触|バリア接触|コースアウト|大減速|停止回数|最大停止s|最大車線誤差m|目標±0.75m内の前進距離m|
|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
|-0.75|Player1_Car|完了|2|2|3867.252|50.689|1|21|0|0|0|0|18.176|3170.646|
|0|Player2_Car|完了|2|2|3867.629|69.569|0|22|0|2|0|0|18.322|3191.619|
|0.75|Player2_Car|完了|2|2|3867.917|50.845|0|22|0|0|0|0|3.925|3195.526|

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
