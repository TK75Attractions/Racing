# 高速旋回とハンドル減速（2026-10-20）

高速時に操舵倍率と切れ角を小さくし続ける旧処理と、ブースト中の22 m/s²のドリフト保護が、チューブのカーブで旋回不足を起こしていた。既定ONの高速旋回では操舵倍率を0.65以上残し、通常操舵とドリフト保護の横加速度目安を速度に応じて最大48 m/s²へ広げる。深く切ると追加減速と持続ブースト加速の抑制が働く。F8の `High-speed cornering + turn braking` で旧挙動と比較できる。

## 本番車両の物理比較

`HighSpeedCorneringValidation.Run` は本番 `car2.prefab`、接地判定、タイヤ力、姿勢制御、PhysXを使用する。初期速度40／60 m/s、最大チャージ相当30 m/s²のドリフトブースト、左右入力±12、平地と左右60度の傾斜でON/OFFを比較した。開始時の初期条件設定後は、入力と通常の車両処理だけで1秒間走らせる。空中や停止状態に軌道補正を加える検証ではない。

| 条件（平地・右入力12） | OFF | ON |
| --- | ---: | ---: |
| 40 m/s開始、1秒後の進行方向変化 | 約27度 | 約65度 |
| 同、1秒後の速度 | 約62 m/s | 約31 m/s |
| 60 m/s開始、1秒後の進行方向変化 | 約22度 | 約76度 |
| 同、1秒後の速度 | 約72 m/s | 約35 m/s |

傾斜左右でも同等の結果となった。最大横滑り角は約12.6度以内、路面に対する最大傾きは0.1度未満。直進ブーストの速度は両モードで同じ。中央±0.07のノイズには操舵・追加減速なし。±3の小さな修正でも進行方向がより曲がることを確認した。合計31走行と、接地状態での走行中ON/OFF、接地から空中への切替、入力停止、切断の確認を含む。`physics.json`に全走行を保存した。

## 実コースのチューブ比較

`HighSpeedCorneringValidation.RunTube` はSampleScene内、進捗1528m付近の43度/30mのチューブカーブを使う。35／45 m/sから、正走・逆走それぞれ最大ドリフトブースト中に走らせる。ボットは各ステップで現在の車の位置・回転・速度・角速度と前方コースを観測し、ペダルとハンドルだけを決める。最初の配置・速度設定後のワープ、速度の直接設定、コース復帰は使わない。

| 初速・方向 | 最大横ずれOFF | 最大横ずれON |
| --- | ---: | ---: |
| 35 m/s・正走 | 12.89 m | 0.93 m |
| 35 m/s・逆走 | 12.47 m | 0.75 m |
| 45 m/s・正走 | 13.32 m | 0.54 m |
| 45 m/s・逆走 | 13.61 m | 0.51 m |

ONは2秒間で78〜87m進み、走行可能領域の逸脱なし。OFFの正走は壁へ到達してほぼ停止した。チューブ全体が走行可能なので、旧挙動の横ずれをそのままコース外判定とは扱っていない。ONはカーブ後に減速要求がなくなると加速し、2秒後は約44〜47 m/sで走行を継続した。`tube.json`と各ステップの`tube-trace.csv`に記録した。

## 検証方法

```sh
bash Tools/Validate-Rgp.sh
Unity -batchmode -projectPath "$PWD" -executeMethod HighSpeedCorneringValidation.Run -quit -logFile /tmp/cornering-physics.log
Unity -batchmode -projectPath "$PWD" -executeMethod HighSpeedCorneringValidation.RunTube -quit -logFile /tmp/cornering-tube.log
RACING_STABILITY_OUTPUT=/tmp/cornering-stability Unity -batchmode -projectPath "$PWD" -executeMethod HighSpeedCorneringValidation.RunGameplay -logFile /tmp/cornering-gameplay.log
```

`Unity`はUnity 6000.3.9f1の実行ファイルを指定する。`RunGameplay`は日本語表示の静的フォントと既存機能を確認し、本番車両のPlay Mode安定性試験を開始するため`-quit`を付けない。減速が実際に働いたスピン保護試験では、速度維持の旧条件を「有用な前進速度を保つ」条件に更新し、横滑り・旋回速度・転倒・空中の条件は維持した。

既存の純粋C#テスト97件、日本語HUDの342文字、P2入力切替／着地報酬等の機能検証、本番車両のPlay Mode安定性58ケース、段差9ケースと路面端からの離陸検証を通過した。検証マーカーは`validation.txt`、安定性の全ケースは`stability-report.json`に保存した。安定性試験の専用広場ではコース復帰を停止して計測への割込みを防ぎ、離陸は本番コースと同じ路面端から行う。通常ゲームの段差対策・自動復帰・ジャンプ処理は変更していない。

## 通常レースの実走

Experienceの1周、`skill-club-expert`（入力ボット20／28 m/s、アイテムあり）を本番SampleSceneで実行した。車の状態を見て通常入力を決め、レース中の位置・速度や周回数は直接書き換えない。4台すべて完走、最大停止時間0秒、コース逸脱0秒、全車復帰0回だった。P1でハンドル減速が0.82秒、最大14 m/s²作動した。P2・CPUはこの走行では追加減速が必要な速度と操舵の組み合わせにならなかった。実走区間の記録は`runtimeErrors=[]`で、Unity起動時のSearch索引処理等はこの計測に含まない。`race-report.json`に全車の結果と方法を保存した。

再実行:

```sh
RACING_FEATURE_PRESET=experience RACING_BALANCE_SKILL_SUITE=1 \
RACING_BALANCE_RUN=skill-club-expert RACING_BALANCE_TIME_SCALE=3 \
RACING_BALANCE_OUTPUT=/tmp/cornering-race \
Unity -batchmode -projectPath "$PWD" -executeMethod SteeringComfortValidation.RunCourseBatch -logFile /tmp/cornering-race.log
```

実ハンドルでの試走と人による操作感評価は未実施。
