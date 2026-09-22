# 速度に応じた曲がりやすさ

各車の `DebugMover` が、速度から前輪の操舵角にかける倍率を決めます。遅いときはハンドル入力どおりに曲がり、速くなるほど同じハンドル入力でも切れ角が小さくなります。

- `Steering Fade Start Speed`（初期値 10 m/s）までは倍率 1 で、低速では最も曲がりやすい状態です。
- そこから `Steering Fade Full Speed`（初期値 30 m/s）に向かって倍率が下がり、`High Speed Steering Multiplier`（初期値 0.35）に達します。
- 全速度でこの倍率が下限として残り、全速度域で `Max Steering Angle`（初期値 30 度）の上限も併せて働きます。
- ドリフトの開始・チャージ判定は生のハンドル入力を見るため、この補正の影響を受けません。
- リスポーンや走行不可の間は操舵角 0、倍率 1 に戻します。

## 調整

車の Inspector の `DebugMover > Steering` で調整します。

| 項目 | 意味 | 初期値 |
| --- | --- | --- |
| Steering Input Multiplier | ハンドル入力を角度に変換する倍率 | 1 |
| Max Steering Angle | 前輪の最大操舵角（度） | 30 |
| Steering Fade Start Speed | 曲がりにくくし始める速度（m/s） | 10 |
| Steering Fade Full Speed | 高速時の倍率に達する速度（m/s） | 30 |
| High Speed Steering Multiplier | 高速時に残す操舵角の割合 | 0.35 |
| Steering Fade Sharpness | 変化の曲線の形 | 1 |
| Enable Speed Sensitive Steering | 速度による補正の有効・無効 | 有効 |

`Steering Fade Sharpness` は変化の形だけを変え、両端の値は変えません。

- 1：開始速度から全速度まで直線的に下がります。
- 1 より大きい：低速〜中速では効きを保ち、高速側で一気に曲がりにくくなります（2、20 m/s で倍率 0.8375）。
- 1 より小さい：開始速度を超えた直後から大きく曲がりにくくなります（0.5、20 m/s で倍率 約 0.54）。

もっと曲がりにくくしたい場合は `High Speed Steering Multiplier` を下げ、効き始めを早めたい場合は `Steering Fade Start Speed` を下げます。旋回の速さ（ヨー角速度）はおおよそ「速度 × 操舵角」に比例するため、最高速付近でも曲がりにくさを体感させるには `Steering Fade Full Speed` を実際の最高速に近づけ、`High Speed Steering Multiplier` を小さめにするのが有効です。初期設定では 30 m/s で補正が頭打ちになるため、それ以上の速度では速いほど旋回自体は速くなります。

## 確認

`Runtime Monitor` の `Speed Meters Per Second`、`Applied Steering Angle`、`Applied Steering Multiplier` で走行中の状態を確認できます。コードからは `SpeedMetersPerSecond` と `SteeringSpeedMultiplier` を参照できます。

Unity メニューの `Racing > Validate Steering` は、低速での倍率 1、範囲内の変化、高速側の下限、速度上昇で切れ角が戻らないこと、最大操舵角の上限、鋭さの効き方、開始速度と到達速度を逆転させた場合、補正の無効化、リスポーン時の初期化を検証します。バッチ実行は `-batchmode -nographics -executeMethod SteeringValidation.Run -quit` を使用します。

実走では、低速のコーナーと最高速での車線変更の両方を、両プレイヤーで確認してください。
