# レース開始前のドローン風コース紹介

通常の準備完了後・再レース開始前に、約9秒のコース紹介を再生する。

1. 校舎と地上コースが見える上空約260mから開始。
2. 約7.25秒かけて校舎正面へ曲線的に下降（終点の高さ約28m）。位置・注視点を滑らかに変化させ、カメラのロールは付けない。
3. 残り約1.75秒で、各プレイヤーのスタート車両の後方へ移動する。
4. 通常の3秒カウントダウンを最初から表示し、レースを開始する。

P1/P2の上空ショットは同期し、車の後方へ戻る部分だけ各プレイヤーのカメラへ接続する。紹介中は走行HUDを隠し、コース名とレターボックスを表示する。車両の入力許可、レース時計、カウントダウンは進めない。物理時間や車両姿勢は変更しない。

`CourseIntroDirector` は既存の `Countdown` 状態の先頭で実行する。`StartGameWhenScreenCovered` でカメラを用意するので、通常の暗転から上空映像が出る。`CompleteGameStart` 後に紹介を進め、終了してから既存カウントダウンを動かす。結果・タイム計測・周回判定の状態は追加しない。

F8の **Pre-race drone course preview** でOFFにすると紹介を省略できる。再生中のOFFもカメラ・HUDを復元して通常カウントダウンへ進む。カメラ経路の座標は現在の `SampleScene` の校舎向け。別コースへ移す場合は `CourseIntroDirector` を事前にGmanagerへ付け、Inspectorの経路・注視点を調整する。

上空で地面モデルの矩形の端が空に浮いて見えないよう、紹介中だけ既存の地面Materialを使う背景Planeを足す。Colliderは無効で、紹介終了・中断時に削除する。ゲーム中の地形・衝突形状やMaterial資産は変更しない。

## 動画と確認

`Artifacts/CourseIntro/Final/course-intro.mp4` は実際のP1カメラを24fps・無音で収録したもの。ゲーム時計に合わせたオフライン収録であり、描画性能の測定ではない。通常の開始フローから撮影し、テスト用に準備画面だけ省略する。

```sh
RACING_BALANCE_SKILL_SUITE=1 \
RACING_BALANCE_RUN=human-fast-control-7102 \
RACING_BALANCE_VIDEO=1 RACING_BALANCE_WINDOW_SECONDS=5 \
RACING_COURSE_INTRO_CHECK=1 \
RACING_BALANCE_OUTPUT="$PWD/Logs/CourseIntro/Preview" bash Tools/RunBalancePlaytest.sh
```

`CourseIntroPlaytestProbe` は両画面の同期、紹介中の走行禁止・時計停止・グリッド保持、紹介後の全3秒カウントダウンとカメラ・HUD・背景の破棄を観測する。`RACING_COURSE_INTRO_MODE=off` で2.5秒後にF8相当のOFF、`restart` でコンポーネントを一度無効化し、同じフレームに再開する。
