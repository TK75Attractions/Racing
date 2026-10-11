# 発光するチューブ壁面と試走写真（2026-10-10）

シアンと紫の側壁ライン、二重矢印、20m間隔のアーチを追加。
48m周期の明るい帯が14m/sで車へ向かって滑らかに流れます。
元のチューブと同じメッシュを内面へ3cmずらして描く方式で、道路・地形・Colliderは変更していません。
光源・テクスチャを増やさず、追加の透明描画1枚と既存のURP/Bloomを使います。

## 写真

Unity 6000.3.9f1のPlay Modeで、実際のcar2 Prefabをペダルとハンドルだけで走らせて撮影。
ゲーム既存のP1撮影用カメラ（BackImageCameraとそのカメラスタック）を1600×900で描画しています。
車の配置・カメラ位置・照明・色調・既存エフェクトを写真用に変更していません。
Batch Modeでも試走中の各フレームを描画し、モーションブラーの履歴を保持しています。

- `01-entry-descent.png`: 東側入口の下り、22.12m/s。
- `02-deep-straight.png`: 地下直線、20.03m/s。
- `03-underground-curve.png`: 地下カーブ、20.00m/s。
- `04-west-exit.png`: 西側出口への上り、21.20m/s。

写真の車位置とゲーム時刻は `frames.json`、全走行記録は `trace.csv` に保存。

## 検証結果

- P1・P2とも一周と12mジャンプの離陸・着地に成功。
- 停止・壁接触・転倒なし。最大傾斜24.09°、意図したジャンプ以外の全輪非接地は最大0.02秒。
- 4秒のコース逸脱後も自動復帰なし。
- 道路の反転・地形との重なり・地下管壁の走行障害は0。チューブの元メッシュと主地面99.77%を保持。
- 新Shaderのコンパイルエラーなし。4枚すべてを目視確認。

`Racing > Capture Tube Gameplay (Play Mode)` で再撮影できます。
バッチ: `-batchmode -executeMethod TubeGameplayCapture.RunBatch`（`-nographics` と `-quit` は付けない）。
撮影先は `RACING_TUBE_CAPTURE_OUTPUT`、走行記録の保存先は `RACING_COURSE_PLAYTHROUGH_OUTPUT` で指定可能。

色・発光強度・アニメーションの速さは `Assets/Scenes/TubeCourseData/TubeSpeedPatterns.mat` で変更できます。
