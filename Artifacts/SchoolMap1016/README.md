# 1016校舎マップへの差し替え

添付の `1016こうしゃ.fbx` を `Assets/Scenes/1016こうしゃ.fbx` として使用。旧マップのGUIDを引き継ぎ、SampleSceneのマップ参照と既存の設定を維持しています。添付との一致は `source.json` のSHA-256で確認できます。

新モデルの133断面からチューブの中心線を取得し、西側の重複する折り返しを省いた121点を走行線に使用しました。地上区間を含めて130点、全長1,904.19mです。新しい東側の管壁と入口に追従し、12mのジャンプをX=584〜596mへ移動。着地後の23mを直線にしてから入口へ曲げることで、バリアの折り返しと空中での接触を解消しています。

地面の開口、入口・坂の路面クリアランス、内壁の両面Collider、発光模様の長さ、チェックポイント・アイテムなどの位置を再生成しました。元のチューブ表示メッシュは保持しています。

## 確認結果

- `TubeCourseValidation.Run`: 合格。道路の反転、地形との重なり、チューブ壁の障害はすべて0。主地面の残存面積は約100%。最大方向変化15.53°/5m、最大勾配0.446、最大勾配変化0.086/5m。
- `RaceCourseRoadValidation.Run` / `RaceCourseValidation.Run`: 合格。道路と範囲判定の一致、衝突面、坂・模様・継ぎ目・再生成、Rigidbodyによる接地を確認。
- `TubeGameplayCapture.RunBatch`: 合格。実際のP1/P2車両をペダル・ハンドル入力のみで一周させ、ジャンプと着地、壁接触0、転倒なしを確認。意図しない連続離陸はP1が0秒、P2が最大0.18秒（上限0.3秒）。コース外に4秒置いた車が自動で戻されないことも確認。
- 走行検証は生成バリアとの接触も検出するよう修正済み。

`report.json` に形状検査、`Playthrough/report.json` と `trace.csv` に実車試走の結果、`Gameplay/` に実際のP1カメラの4地点の画像を保存しています。

## 再生成

Unityの `Racing > Align Course To 1016 School Map` でコースと地形・配置・発光模様を再生成し、形状検査まで実行できます。この操作はSampleSceneを保存します。手動で調整したコースを保って地形だけ更新する場合は `Racing > Prepare Tube Course Terrain` を使用します。

試走・撮影は `Racing > Capture Tube Gameplay (Play Mode)`。バッチ実行時は `-executeMethod TubeGameplayCapture.RunBatch` を指定し、`-nographics` と `-quit` は付けません。結果の保存先は `RACING_COURSE_PLAYTHROUGH_OUTPUT`、画像の保存先は `RACING_TUBE_CAPTURE_OUTPUT` で指定できます。
