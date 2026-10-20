# 自動運転・CPU・ボットのペース（2026-10-20）

現コースは1周1904.19m、2周3808.39m。2周120秒には停止・カーブ・加減速を含めて平均31.74m/s（114.25km/h）が必要。従来はP2自動運転が24m/s、CPUが25〜30m/sを上限に人間へ合わせ、カーブでは一律14m/sまで落とす処理もあった。

## これまでの計測

単位は公式ゲーム内の完走タイム。1周と2周、アイテム・車両設定・ドライバー役の違いがあるため、同一条件での比較ではない。現在の2周2分目標に合わせて1周の結果を単純に2倍した値は使わない。

| 条件・記録 | 周回 | P1 | P2 | CPU1 | CPU2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 初期操舵調整 / equal-control-101 | 2 | 3:09.36 | 3:01.86 | DNF | DNF |
| 攻防調整 / skill-rivalry | 2 | 3:42.37 | 3:34.05 | 3:41.77 | 3:34.05 |
| 攻防調整 / skill-club-expert | 2 | 3:44.58 | 2:51.55 | 3:44.58 | 2:51.55 |
| P2自動運転・アシスト負荷試験 / p2-stress | 1 | 1:56.16 | 1:33.09（AUTO） | 1:54.33 | 1:33.09 |
| 高速旋回調整後 / skill-club-expert | 1 | 1:52.10 | 1:29.61 | 1:50.60 | 1:29.61 |

既存のJSON走行報告から抽出できた全180台分を`historical-times.csv`に保存した。各行に出典、記録日時、コース長、周回、入力役、DNFを残している。古いコースや途中段階の失敗結果も含む。詳細出典はCSVのsource列を参照する。

## 高速化後の2周計測

本番SampleScene、通常の2周、アイテムなし。GOからの公式ゲーム内タイムで計測した。車の状態とコースを観測してペダル・ハンドルを決め、通常の車両物理とゴール判定で完走する。計測中に車の位置・速度・周回数・結果を書き換えない。時間倍率3は壁時計の待ち時間だけを短縮し、固定物理刻みは20msのまま。

| 乱数条件・入力役 | P1 | P2 | CPU1 | CPU2 |
| --- | ---: | ---: | ---: | ---: |
| 6201 / P1高速ボット・P2本番自動運転 | 1:45.70 | 1:33.70 | 1:44.20 | 1:38.20 |
| 6202 / P1・P2とも高速ボット | 1:46.99 | 1:36.49 | 1:43.99 | 1:39.49 |

両条件とも全車2周2分以内、コース逸脱0秒、復帰0回、最大停止時間0秒、実走区間のruntimeErrors=[]だった。1回目の実測平均速度はP1約131km/h、P2自動運転約147km/h、CPU約133／141km/h。`autodrive-two-laps.json`と`bots-two-laps.json`に設定と全車結果を保存した。Unity起動時の既知のSearch索引処理等は実走区間のエラー計測に含めない。

高速AIペースはF8でON/OFFできる。共通の通常巡航目標を50m/sにし、5m刻みで100m先までの曲率と制動距離からカーブの速度を計画する。ジャンプでは32m/sへ事前減速し、接触回避・車間確保を優先する。CPUは遠い低速相手に引きずられずに走り、35m以内の高速相手とは速度窓による攻防を続ける。明示的な低速・初心者役の試走は比較用に残した。

再実行:

```sh
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=skill-fast-pace RACING_P2_AUTO=1 \
RACING_BALANCE_TIME_SCALE=3 RACING_BALANCE_OUTPUT=/tmp/racing-fast-auto \
Unity -batchmode -projectPath "$PWD" -executeMethod SteeringComfortValidation.RunCourseBatch -logFile /tmp/racing-fast-auto.log

RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=skill-fast-pace-2 \
RACING_BALANCE_TIME_SCALE=3 RACING_BALANCE_OUTPUT=/tmp/racing-fast-bots \
Unity -batchmode -projectPath "$PWD" -executeMethod SteeringComfortValidation.RunCourseBatch -logFile /tmp/racing-fast-bots.log
```

`Unity`はUnity 6000.3.9f1の実行ファイルを指定する。実機ハンドルの入力遅延・人の楽しさ・描画負荷の評価は別途必要。低速プレイヤーや妨害、事故があるすべてのレースで2分を保証するものではない。
