# タイトル・結果画面のコース背景

`MenuSceneryDirector` が既存のプレイヤー別タイトルカメラを使い、実際の校舎・コース・植栽・街並みを表示する。

- タイトルは校舎全体と地上コースが見える上空約220mから。約120秒で滑らかに往復する。
- 結果は校舎正面・スタート付近が見える上空約130mから。メダル・順位・本日のランキングの間に景色を見せる。
- タイトルロゴはUIの文字として重ねる。右側の操作案内と画面の縁に薄いグラデーションを入れ、文字を読みやすくする。
- 天候・時間帯・校舎の装飾はそのゲーム内の環境設定を反映する。俯瞰中はコース紹介と共通の遠景用シャドウ設定と地面の延長を使い、レース開始時に解除する。
- 新しいカメラや常時描画するRenderTextureは増やさない。地面の延長に当たり判定は付けない。

P1/P2は個別の画面状態から構図を選ぶ。一方が結果、もう一方がタイトルでもそれぞれの背景を保つ。チュートリアル・コース紹介・レース中の追従カメラは既存の処理へ戻す。車選択の3Dショールームは引き続き専用の画面で表示する。

F8の **Live campus scenery on title / results**、または管理画面の **タイトル・結果画面の校舎背景** で即時ON/OFFできる（初期値ON）。OFFは元の静止画背景へ戻し、メニュー用カメラの位置・レンズも復元する。結果画面右上のコース案内は旗と実コース形状の図を表示する。

検証:

```sh
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" -executeMethod MenuSceneryValidation.RunBatch \
  -logFile Logs/MenuSceneryPlaythrough.log
```

実際のPlay Modeで、両プレイヤーの背景、片方だけタイトル復帰、F8 OFF/ON、コンポーネントの無効化・再有効化、紹介・カウントダウン後のレースカメラ復帰を確認する。`Logs/MenuScenery` に画面のJPEGと検証結果を保存する。保存先は `RACING_MENU_SCENERY_OUTPUT` で変更できる。代表画面は `Artifacts/MenuScenery` に記録する。
