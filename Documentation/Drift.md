# ドリフト操作

各車の `DebugMover` が、その車に割り当てられたハンドル入力からドリフトを制御します。

- 生のハンドル入力の絶対値が 8 以上になると開始します。高速時の操舵角補正には影響されません。
- ドリフト中は速度抵抗が通常の 1.25 倍、後輪の横グリップは初期値で 90% になります。1 より大きい値を設定すると通常以上のグリップになります。
- ハンドルを大きく切るほどチャージが速くたまります。絶対値 30 以上で毎秒 1、上限は 3 です。
- ハンドルを中央（0）に戻しても状態とチャージを維持します。中央ではチャージは増えません。
- 開始時と逆符号の入力で解除し、初期設定ではチャージ × 3 m/s² の加速度で 1 秒間、車体の水平方向の前方に加速します。解除と同時にチャージを消費します。
- 逆方向に大きく切り続けると、解除の次の物理フレームから新しいドリフトが始まります。
- 加速中に再解放した場合は、新しいチャージ量による加速度と設定時間で置き換えます。加速度は重ね合わせません。
- リスポーン、入力源の変更、走行不可、コンポーネントの無効化ではチャージを破棄し、持続中の加速も停止します。
- 溜まっている量は車体の火花の色と画面端の色で分かります。「チャージ量の表示」を参照してください。

初期設定では速度・接地・ペダル入力を開始条件に含めていません。

## 調整と確認

車の Inspector の `DebugMover > Drift` で開始入力、最大チャージ速度に達する入力、チャージ上限、蓄積速度、抵抗倍率、後輪グリップを調整できます。

`DebugMover > Drift Boost` で解放時の加速を調整できます。

- `Drift Boost Duration`：加速時間（秒）。初期値 1。0 にすると加速しません。
- `Drift Boost Acceleration Per Charge`：チャージ 1 あたりの加速度（m/s²）。初期値 3。実際の加速度は「解放時のチャージ × この値」です。

例：チャージ 3、加速時間 2 秒、加速度設定 4 の場合、12 m/s² で 2 秒間加速します。抵抗やタイヤから受ける力を除いた速度増分は 24 m/s です。以前の `Drift Boost Speed Per Charge` の保存値は、新しい加速度設定に引き継がれます。

`Runtime Monitor` の `Is Drifting`、`Drift Charge`、`Drift Boost Time Remaining`、`Active Drift Boost Acceleration` で状態を確認できます。コードからは `IsDrifting`、`DriftCharge`、`NormalizedDriftCharge` を参照できます。

キーボードの左右入力は ±10 なので、初期設定でドリフトを試せます。約 9 秒で満充電になり、逆ハンドルで 9 m/s² の加速が 1 秒間続きます。

Unity メニューの `Racing > Validate Drift` は、開始境界、中立経由の解除、左右両方向、チャージ上限、時間刻みへの独立性、抵抗倍率、状態の初期化、加速時間・加速度設定、加速終了と再解放を検証します。バッチ実行は `-batchmode -nographics -executeMethod DriftValidation.Run -quit` を使用します。

実走では、両プレイヤーで旋回中の滑り方、減速量、逆ハンドル後の加速感を確認し、コースに合わせて調整してください。

## チャージ量の表示

チャージがどれだけ溜まっているかは、**車体の火花**と**画面端の色**の 2 つで示します。HUD には数値やゲージを出しません。走行中に視線を前方から外さずに読み取れることを優先しています。

段階は表示のためだけのもので、解放時の加速量は従来どおりチャージ量に比例した連続値です。

- `DebugMover > Drift Charge Display > Drift Charge Tier Thresholds`（初期値 0.34 / 0.67 / 1）が段階の境目です。正規化チャージ（0〜1）で指定します。
- 段階の色は `DriftChargeVisual > Tier Colors` の 1 か所だけで定義します。火花も画面端も同じ値を使うため、色がずれることはありません。初期値は 段階0 白 → 段階1 青 → 段階2 橙 → 段階3 紫です。

### 車体の火花

各車に `DriftChargeVisual` が付き、後輪から段階の色の火花を出します。衝突火花と同じ加算シェーダーを共有し、専用のアセットは不要です。`Gmanager` が車の生成時に自動で追加するため、プレハブの編集は要りません。

- ドリフト中だけ発生し、段階が上がるほど量が増えます。
- 段階が上がった瞬間に弾けます。解放の瞬間は、直前の段階の色でより大きく弾けます。
- 満チャージ中は量が脈動し、「解放できる」ことを伝えます。
- 車体に付くため相手の画面にも映ります。相手がどこまで溜めているかが見えるので、駆け引きの材料になります。
- 停車に近い速度（初期値 2 m/s 未満）では出しません。走行不可・リスポーン・完走後は止まります。

`DriftChargeVisual` の Inspector で発生量（`Sparks Per Second Per Wheel`、`Tier Emission Bonus`）、段階上昇と解放の弾け方（`Tier Up Burst`、`Release Burst`）、`Minimum Speed`、`Full Charge Pulse Hz` を調整できます。

### 画面端の色

`VManager` が、プレイヤーごとの専用 Volume で画面端を段階の色に染めます。加速演出と同じプレイヤー専用レイヤーを使い、優先度だけを速度演出と加速演出の間に置くため、新しいレイヤーは不要で、相手の画面には影響しません。

- 強さはチャージ量に比例します。満チャージで最大になり、脈動します。
- 解放するとチャージが 0 になるため自然に消え、入れ替わりで加速演出が出ます。
- リスポーン・走行不可・リザルトでは即座に解除します。

`VManager` の Inspector の `Drift Charge Post Processing` で調整できます。

| 項目 | 意味 | 初期値 |
| --- | --- | --- |
| Drift Charge Effects Enabled | チャージの画面演出の有効・無効 | 有効 |
| Charge Max Weight | 満チャージ時の演出の強さ | 0.9 |
| Charge Fade Seconds | チャージ量に追従する速さ | 0.18 秒 |
| Charge Vignette Intensity | 画面端の暗さの加算量 | 0.2 |
| Charge Vignette Smoothness | 画面端のぼかし | 0.45 |
| Charge Chromatic Intensity | 色収差の加算量 | 0.1 |
| Charge Full Pulse Hz | 満チャージ時の脈動の速さ | 2.4 Hz |
| Charge Full Pulse Depth | 満チャージ時の脈動の深さ | 0.35 |

### 検証

Unity メニューの `Racing > Validate Drift Charge` は、段階しきい値の境界、満チャージの判定、リスポーンでの初期化、段階と色の対応（隣り合う段階が別色であること）、チャージ量への追従とフェード、画面端の色が段階色になること、プレイヤー間の独立、`Gmanager` 経由の連携、リスポーン・リザルトでの解除、演出を無効にしたときの動作を検証します。バッチ実行は `-batchmode -nographics -executeMethod DriftChargeValidation.Run -quit` を使用します。

実走では、両プレイヤーで段階の切り替わりが分かるか、画面端の色が走行の邪魔になっていないか、満チャージに達するまでの時間（初期設定で約 9 秒）が長すぎないかを確認してください。長いと感じる場合は `Drift Charge Per Second` を上げてください。

## 加速中の画面演出

`Gmanager` は各車の `BoostVisualIntensity` を描画前に読み取り、参照先の `VManager` にプレイヤー別の演出強度を渡します。チャージ中には演出せず、解放後の加速中にチャージ量に比例した演出を表示します。加速度盤も同じ値と経路を共有します。加速終了・リスポーン・完走後にはフェードアウトし、タイトル・カウントダウン・リザルトへの切り替え時には即座に解除します。

`SampleScene` の `GameManagers/VManager` に `Volume` と `VManager` を接続済みです。既存の画面構成と同じ URP のオーバーライドを使用します。通常設定のアセットは変更せず、実行時のプロファイルだけを変更します。

VManager の Inspector の `Drift Boost Post Processing` で以下を調整できます。

| 項目 | 意味 | 初期値 |
| --- | --- | --- |
| Drift Boost Effects Enabled | 加速演出の有効・無効 | 有効 |
| Boost Fade In Seconds | 演出が最大強度に達する時間 | 0.08 秒 |
| Boost Fade Out Seconds | 通常の画面に戻る時間 | 0.25 秒 |
| Boost Bloom | 光のにじみの加算量 | 0.5 |
| Boost Motion Blur | カメラ移動によるブラーの加算量 | 0.4 |
| Boost Lens Distortion | レンズの歪みの加算量 | -0.12 |
| Boost Chromatic Aberration | 色収差の加算量 | 0.15 |
| Boost Vignette | 画面周辺の暗さの加算量 | 0.12 |
| Boost Post Exposure | 露出の加算量 | 0.15 |
| Boost Contrast | コントラストの加算量 | 10 |

各加算量は満チャージ時の値です。通常の歪みなどを保持したまま、専用 Volume の weight を補間して演出を重ねます。`BoostVolumeP1`（レイヤー10）と `BoostVolumeP2`（レイヤー11）は画面演出専用です。各メインカメラのみが自分のレイヤーを参照するため、相手の画面や後段の UI カメラには加速演出を適用しません。

通常時の画面設定も `Gmanager.Control.VManager.SetBloom(0.9f)` や `SetMotionBlur`、`SetLensDistortion`、`SetVignette`、`SetColorAdjustments` などから調整できます。演出終了時にはこれらの通常設定へ戻ります。

Unity メニューの `Racing > Validate Boost Volumes` で、URP Volume への反映、プレイヤー間の分離、フェード、通常設定の復帰、Gmanager 経由の連携とリスポーン・リザルト時の解除を検証できます。バッチ実行は `-batchmode -nographics -executeMethod BoostVolumeValidation.Run -quit` を使用します。ブラーや歪みの操作感は Game ビューで実走して調整してください。
