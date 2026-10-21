# 急操舵の安定化（2026-10-21）

対象: `1e8cf6f`、Unity 6000.3.9f1。本番 `car2` Prefab、SampleScene。

## 修正

通常走行中も接地面に沿ってヨー回転を制御し、急な切り込み・切り返しでの横滑りを約1.5度の許容幅へ戻す。ドリフトで後輪のグリップを落とさず、チャージ／ブーストと高速旋回時の減速は維持する。F8の `Stable steering: prevent sudden slip / spin` で走行中に比較できる。

## 本番車両の物理試験

[physics.json](physics.json): 20／40／60 m/s、左右の入力±6／±30、平地・左右60度の接地、最大ブースト、短い切り込み、左右切り返し、直進をOFF／ONで86ケース比較。初期配置・初速を設定した後はハンドル／ペダルから本番物理処理を実行し、車体姿勢を直接修正しない。

| 全比較ケース中の最大値 | OFF | ON |
| --- | ---: | ---: |
| 横滑り角 | 12.58° | 1.37° |
| ヨー角速度 | 2.05 rad/s | 1.13 rad/s |

ONの左右旋回は実際の進行方向がハンドルの方向へ曲がることも検査。直進ブーストの加速はOFFと同等。空中・後退・切断・オイル・スピンは別途同じ初期状態でOFF／ONを物理シミュレーションし、速度／角速度が変わらないことを検査した。

[core-test-results.xml](core-test-results.xml): Coreの118テスト全通過。

```sh
Unity -batchmode -nographics -projectPath "$PWD" \
  -executeMethod AbruptSteeringValidation.Run -quit -logFile /tmp/abrupt-steering.log
bash Tools/Validate-Rgp.sh
```

## 人間相当の入力による2周試走

[report.json](course/report.json) / [summary.json](course/summary.json) / [trace.csv](course/trace.csv)。`human-fast-control-7102`、アイテム無効、CPUは通常動作。P1／P2ボットはコース形状・車体状態・前方車両を観察し、反応間隔0.18／0.22秒、物理ハンドル換算360度/秒までの手の動き、操舵ノイズを含むモデルでペダルと操舵を入力。初速・位置・チェックポイント・結果の書き換えは行わない。リセット補助が有効な計画だが、今回は全車で復帰・リセットは発生していない。

| 車両 | 2周完走 | 平均速度 | 最高速度 |
| --- | ---: | ---: | ---: |
| P1 | 1:50.99 | 127.8 km/h | 184.8 km/h |
| P2 | 2:11.99 | 112.3 km/h | 170.6 km/h |
| CPU 1 | 2:02.99 | 111.9 km/h | 162.5 km/h |
| CPU 2 | 1:47.99 | 127.4 km/h | 165.9 km/h |

全車でコース逸脱0秒。ゲーム実行中の例外0。単一試走の結果であり、人間の操作感や実機通信遅延を評価したものではない。

```sh
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=human-fast-control-7102 \
RACING_BALANCE_TIME_SCALE=6 RACING_BALANCE_OUTPUT=Logs/SteeringStability/Course \
Unity -batchmode -projectPath "$PWD" \
  -executeMethod SteeringComfortValidation.RunCourseBatch -logFile /tmp/steering-course.log
```

## チューブ旋回の回帰試験

[tube.json](tube.json): 本番の43度/30mのチューブカーブへ35／45 m/sで入り、最大ブースト中に観測からペダル／操舵のみを入力。正逆走とも高速旋回制御ONでコース外0秒、25m以上の移動を達成し、同制御OFFよりライン保持が改善した。ここの `enabled` は既存の **HighSpeedCornering** フラグであり、新しいSteeringStabilityは両群ともON。

```sh
RACING_CORNERING_OUTPUT=Logs/SteeringStability/Tube \
Unity -batchmode -nographics -projectPath "$PWD" \
  -executeMethod HighSpeedCorneringValidation.RunTube -quit -logFile /tmp/steering-tube.log
```
