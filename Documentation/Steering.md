# ハンドル角と旋回の対応

標準設定では、ハンドルを中央から左右それぞれ **90°** 回すと最大まで曲がります。45°ではその速度での最大旋回曲率の半分、15°では約1/6を要求します。90°を超えて回しても切れ角は増えません。

以前は、高速時に前輪角を一律に制限していたため、45°と90°で旋回量がほぼ同じになる場合がありました。現在は速度に応じた上限の内側で旋回曲率を比例させます。小さく切ったときは細かく、大きく切ったときはそれに応じて曲がります。

## 入力と応答

`InputManager > ESP32 USB Serial > Steering Lock Degrees` が片側の最大角です。SampleSceneと設定クラスの初期値は90°です。RGPの通信値や `Steering Divisor` は維持し、`IDriveSteeringCalibration` で走行側へ最大入力を渡します。車両側では最大を±30の共通単位へ正規化するため、Divisorを変えても物理的な最大角は変わりません。タイトルや車種選択の感度は従来どおりです。

前後輪が接地して走行しているとき、`DebugMover` はタイヤ力に加えて路面法線まわりのトルクを使い、ハンドル角に対応する旋回速度へ追従します。中央戻し・切り返しにも同じ補助が働きます。初期値では追従係数18、最大角加速度30 rad/s²です。新しい補助による位置・姿勢・速度の直接書き換えはありません。

空中、入力抑制中、操作不能アイテム中、オイル中、横タイヤ力を無効にした車では新しい補助を適用しません。通常の後退では旋回方向を反転します。ドリフト中の既存の過大な横滑り回復処理は維持しています。

校正情報を持たないキーボード・CPUなどは従来の最大±30を使います。キーボードの±10は最大の1/3です。ドリフト判定も共通単位を見るので、標準ハンドルでは24°以上を0.18秒保持すると開始し、逆方向6°以上を0.1秒保持すると解除します。最大チャージ速度は90°で達します。

## 速度と設定

速度補正は10 m/sまでは1倍、30 m/sで0.35倍になります。さらに標準では、旋回速度1.6 rad/s・横加速度22 m/s²の範囲から切れ角上限を決めます。最大旋回量は速度に応じて変わりますが、その範囲でハンドル角と曲率の比例関係を保ちます。ドリフト開始時に突然上限が変わらないよう、通常走行から同じ上限を使います。

車の `DebugMover` で調整できます。

| 項目 | 意味 | 初期値 |
| --- | --- | --- |
| Enable Responsive Steering | 比例する曲率と接地時の応答補助 | 有効 |
| Steering Yaw Response | 目標旋回速度への追従係数 | 18 |
| Steering Max Yaw Acceleration | 応答補助の最大角加速度（rad/s²） | 30 |
| Steering Input Multiplier | 正規化入力への倍率。上げると90°より手前で最大になる | 1 |
| Max Steering Angle | 停車・低速時の前輪角上限（度） | 30 |
| Steering Fade Start Speed | 速度補正の開始（m/s） | 10 |
| Steering Fade Full Speed | 高速倍率に達する速度（m/s） | 30 |
| High Speed Steering Multiplier | 高速で残す前輪角上限の割合 | 0.35 |
| Steering Fade Sharpness | 速度補正曲線。1は直線、大きいほど高速側で強く補正 | 1 |
| Enable Speed Sensitive Steering | 上記速度倍率を適用する | 有効 |

旋回速度・横加速度の上限は `Drift Spin Protection` の設定を共有します。`Enable Drift Dynamics` または `Enable Drift Spin Protection` を無効にすると、この上限による前輪角の保護を外します。`Enable Responsive Steering` を無効にすると従来の入力角変換とドリフト時のみの補助に戻ります。

## 検証

`Runtime Monitor` の `Applied Steering Angle` またはコードの `WheelSteeringAngle` が要求前輪角です。`SteeringSpeedMultiplier` は速度倍率だけで、最終的な切れ角には安全上限と比例曲率も反映されます。HUDのハンドル表示も物理90°で最大になります。

`Racing > Validate Steering` で校正値のゲート経由の受け渡し、物理90°、45°の比例、左右対称性と旧モードを検査します。`SteeringResponseValidation.ValidateBatch` は本番車両・タイヤを20ms刻みで動かし、初速8 / 20 / 40 m/s、左右15 / 45 / 90°の18条件と、空中などの補助除外条件、ドリフト・段差回帰を検査します。

修正前後の測定と再現手順は [SteeringResponse](../Artifacts/SteeringResponse/README.md) を参照してください。これらは自動物理テストです。実USBハンドルから画面までの遅延や人が感じる操作感は別途実機で確認が必要です。
