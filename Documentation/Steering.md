# 速度に応じたハンドル補正

`DebugMover` は路面に沿った速度を使い、高速ほど操舵倍率と前輪の切れ角上限を小さくします。IMUの累積角度やRGPの通信値は変更せず、車両へ反映する段階で補正します。前進・後退とも同じ速度補正です。

## 操舵の計算

1. 10 m/sまでは倍率1。10〜30 m/sで従来の倍率1→0.35へ補間します。
2. 30 m/s以降も `0.35 × (30 / 速度)²` で倍率を下げ続けます。ブーストで加速しても旋回が再び鋭くなりません。
3. 前輪角の上限は `min(最大操舵角, atan(横加速度目安 × ホイールベース / 速度²))`。既定の横加速度目安は22 m/s²です。これは旋回要求の上限で、実測横加速度を保証するものではありません。タイヤのグリップ、路面、ドリフト保護も引き続き作用します。
4. 中央±0.08入力単位を不感帯にし、外側では不感帯分だけ差し引いて連続的につなぎます。SampleSceneの除算値6.5では、実ハンドル約±0.52°相当です。
5. 入力を速度別の有効範囲へ収めてから指数平滑化します。既定時定数45ms、中央復帰・切り返しはその55%。過剰な入力をフィルターに蓄積しないので、大きく回した後も切り返せます。加速して上限が縮んだ場合は、過去のフィルター値も即座に上限内へ収めます。

車両Prefab（ホイールベース2.65m、最大角30°）の計算値:

| 速度 | 操舵倍率 | 前輪角上限 |
| --- | ---: | ---: |
| 36 km/h / 10 m/s | 1.000 | 30.00° |
| 72 km/h / 20 m/s | 0.675 | 8.29° |
| 108 km/h / 30 m/s | 0.350 | 3.71° |
| 144 km/h / 40 m/s | 0.197 | 2.09° |
| 180 km/h / 50 m/s | 0.126 | 1.34° |
| 216 km/h / 60 m/s | 0.088 | 0.93° |

ドリフトの開始・チャージ・解放判定は従来どおり補正前の入力を使います。コンフューズによる左右反転は平滑化前に適用します。切断・入力ソースの交換・リスポーン・入力を遮断するアイテム・車両無効化時はフィルターを消去します。

## 画面下端の表示

各プレイヤーの画面下端いっぱいに横線を表示します。

- 白いカーソルと水色の線: 実際に反映した操舵位置。速度補正前の入力目盛りへ戻して表示します。
- 上側の細いマーカー: 入力ハンドル位置。上限外へ回したときは薄いオレンジ色になります。
- 水色の範囲と境界: 現在有効な範囲。上限外の両端は暗くします。
- 制限中は「高速時は操舵角を制限 / 暗い範囲は上限外」と説明します。低速時は中央が直進であることを表示します。

右下の小型ハンドル表示も同じ入力範囲へ正規化し、小さな入力で振り切れないようにしています。

目盛りは車両の最大操舵角と入力倍率を基準にします。IMUの実角度そのものを示す度数計ではありません。観戦時は観戦対象車両に追従します。

## 調整

`DebugMover > Steering` の設定:

| 設定 | 既定値 | 用途 |
| --- | ---: | --- |
| Steering Input Multiplier | 1 | 入力を前輪角へ変換する倍率 |
| Max Steering Angle | 30° | 低速時の最大角 |
| Steering Fade Start Speed | 10 m/s | 倍率の低下開始 |
| Steering Fade Full Speed | 30 m/s | 高速基準倍率へ到達する速度 |
| High Speed Steering Multiplier | 0.35 | 基準速度での倍率 |
| Steering Fade Sharpness | 1 | 基準速度までの補間曲線の形 |
| Enable Speed Sensitive Steering | 有効 | 倍率と速度別角度上限の有効化 |
| Steering Lateral Acceleration | 22 m/s² | 速度別角度上限の計算用 |
| Steering Deadzone | 0.08 | 中央の不感帯（ゲーム入力単位） |
| Steering Response Seconds | 0.045秒 | 平滑化の時定数。0で即時反映 |

`Steering Fade Sharpness` は1より大きいと低速側の効きを保ち、1より小さいと早めに倍率を下げます。速度補正を無効化しても不感帯と平滑化は残ります。

## 検証

`Racing > Validate Steering` は速度域・左右対称・後退・大入力・急加速・微小ノイズ・応答時間・切り返し・上限外入力の蓄積防止・リスポーンを検査します。

`Racing > Validate Steering Comfort (Full Suite)` は上記とドリフト・両者のHUDを確認し、720p/1080pの画面を保存した後、SampleSceneと本番PrefabでPlay Modeの物理試験を実行します。既存の着地・空中・ドリフト回帰試験に、8/20/40/60 m/sの左右旋回・切り返し16ケースと中央ノイズ試験を追加しています。

```sh
RACING_STABILITY_OUTPUT=Artifacts/SteeringComfort/physics \
  /Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" \
  -executeMethod SteeringComfortValidation.RunFullBatch \
  -logFile Artifacts/SteeringComfort/validation.log
```

非同期Play Mode試験なので `-quit` は付けません。画面の描画も検証するため `-nographics` は付けません。試験は外部へのレース公開を無効化します。

自動試験は操舵の計算と物理的な安定性を確認するものです。実機IMUの校正誤差・通信遅延・人が感じる快適さの最終評価には、実ハンドルでの試走が必要です。

実コースで2回の通常レースを記録する場合:

```sh
RACING_BALANCE_RUN=equal-control-101,equal-control-102 \
RACING_BALANCE_TIME_SCALE=3 \
RACING_BALANCE_OUTPUT=Artifacts/SteeringComfort/course \
  /Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" \
  -executeMethod SteeringComfortValidation.RunCourseBatch \
  -logFile Artifacts/SteeringComfort/course-validation.log
```

コースの試走はペダル・ハンドルのみを操作する既存の自動運転役を使います。UIは別途描画検証し、物理試験・コース試走ではカメラの描画を停止して負荷を抑えます。物理試験の待機は描画フレームではなく固定物理時刻を基準にします。
