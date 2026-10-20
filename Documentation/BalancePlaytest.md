# 試走エージェントの構造と仕組み

試走エージェントはUnity Editor専用のルールベース自動運転です。実際の `SampleScene` で、人間の代役としてP1・P2へ入力し、既存CPU2台と走ります。コースを事前に知っている運転で、初見の人の操作感や楽しさは評価しません。

## 構成

| 層 | ファイル | 役割 |
|---|---|---|
| 起動 | `Tools/RunBalancePlaytest.sh` | Unityをバッチ起動し、Gitリビジョンと出力先を渡す。終了後に集計する |
| Editor制御 | `Assets/Editor/BalancePlaytestValidation.cs` | コース実モデルの読み込みを確認し、シーンを開いてPlay Modeへ入る。エージェントを生成し、終了・タイムアウトを管理する |
| 運転・記録 | `Assets/TestAddForce/BalancePlaytestAgent.cs` | 試走条件を順に実行し、入力を生成、全4台の走行とイベントを記録する |
| 集計 | `Tools/AnalyzeBalancePlaytest.py` | 保存済みJSON・CSVから比較用CSV、イベント集計、進捗SVGを作る |

Editorのドメイン再読み込みをまたぐ起動状態は `SessionState` で保持します。試走中はダッシュボードの外部公開を停止します。シーンやPrefabの設定を保存せず、通常の製品ビルドには試走機能を含めません。

## 運転の流れ

1. コースの最寄り進捗、車の位置・向き・水平速度・接地状態を読みます。
2. 速度に応じて8〜17m先を目標にし、さらに25m先の向きからカーブを判断します。カーブでは目標速度を最大14m/sへ下げます。
3. 通常は中心からP1が+2m、P2が-2mのレーンを狙います。30m先までに路面の空白があればジャンプ区間と判断し、中央へ寄って目標24m/sへ加速します。
4. 目標方向との角度と旋回速度からハンドルを、目標速度との差からペダルを計算します。非接地中は両方を中立にします。
5. 約0.02秒ごとの `FixedUpdate` で `IDriveInputSource` に入力を渡します。車両の `DebugMover` が通常の物理処理で走行します。

車両の位置・速度・チェックポイント・結果を運転エージェントから直接設定しません。レースの開始・後片付けは `Gmanager` の既存処理、周回・順位・完走は `LapManager` の判定を使います。復帰支援の条件だけ、停止または逸脱が4秒続いたときに通常のリセット入力を送ります。

## 試走条件と記録

同速2条件、速い側を入れ替えた速度差2条件、両者低速、アイテムあり2条件、復帰支援あり、途中停止の計9条件があります。Unityの乱数seedを記録しますが、物理やCPUの展開が完全に一致する保証はありません。周回数は現在のシーン設定を読み、通常の全体360秒・人間先着後40〜180秒の終了条件を使います。猶予は残距離とそれまでのペースに応じて設定されます。

約0.1物理秒ごとに全車の位置・速度・入力・進捗・周回・順位・アイテム状態を `trace.csv` へ保存します。接触・被弾・逸脱・復帰・停止などは `events.csv`、各試走と全体の集計は `summary.json` と `report.json` に保存します。公式ゲーム時計と物理積算時間は別の値として記録します。

初回の9条件と通常速度の追加観測は `Artifacts/BalancePlaytest/` にあります。この履歴にはGit LFS実モデル未展開という環境不備があり、冒頭に訂正を記載しています。実モデルを復元したうえでのバリア・2周設定の検証は `Artifacts/CourseSafety/` にあります。前後の差をバリアだけの改善率として比較しません。

## 再実行

Git LFSの実データを展開し、このプロジェクトをUnityで開いていない状態で、リポジトリのルートから実行します。

```sh
RACING_BALANCE_OUTPUT="$PWD/Artifacts/CourseSafety/Playtest" \
RACING_BALANCE_TIME_SCALE=3 \
RACING_BALANCE_RUN=equal-control-101,items-402 Tools/RunBalancePlaytest.sh
```

出力先には同名の記録を上書きするため、新しい比較では別フォルダを指定してください。全9条件を実行するには `RACING_BALANCE_RUN` を省略します。Unity内では `Racing > Playtest > Run Balance Agent Suite` から起動できます。

| 環境変数 | 意味 |
|---|---|
| `UNITY_EDITOR` | Unity実行ファイル。既定はmacOSの6000.3.9f1 |
| `RACING_BALANCE_OUTPUT` | 記録先。既定は `Artifacts/BalancePlaytest` |
| `RACING_BALANCE_RUN` | 条件名をカンマ区切りで選択 |
| `RACING_BALANCE_TIME_SCALE` | 時間倍率1〜8。既定8。通常速度の観測は1 |
| `RACING_BALANCE_WINDOW_SECONDS` | 最大360物理秒の観測窓。省略時は自然なレース終了まで |

観測窓で終えた記録は `raceEnded=false` とし、未完走車をDNFとして数えません。加速実行には描画更新と物理更新のタイミング差があります。固定レーンの既知コース運転なので、アイテムを狙う判断、初心者の迷い、実機のハンドル・ペダル、入力遅延や音の分かりやすさは別途人による確認が必要です。

## レベル別の追加試走

`RACING_BALANCE_SKILL_SUITE=1` を指定すると、初心者（反応0.35秒・操舵の揺れ・ジャンプ先読みなし）、慎重（低速・反応0.35秒）、中級（反応0.14秒）、上級（反応0.06秒）、攻める運転（高速・操舵過多）の入力を組み合わせた6条件を実行する。初心者対上級、中級対上級、アイテムありの攻める運転、12秒の途中停止、5秒の接続断、運営による20秒時点の結果表示を含む。

人間役の入力をInputManagerへ接続するため、車両・復帰操作には製品の入力ゲートを使う。コース中心線を知る入力エージェントであり、初見の人の認知や実機操作の評価とは区別する。準備画面は省略する。車両位置・速度・周回・結果は強制設定しない。運営終了条件だけは管理画面と同じShowResult操作を実行する。

```sh
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_TIME_SCALE=6 \
RACING_BALANCE_OUTPUT="$PWD/Artifacts/SkillPlaytest/Recheck" Tools/RunBalancePlaytest.sh
```

初心者・慎重派は、画面にジャンプ案内が表示された場合に0.35秒後に助走するモデルです。再接続・復帰後はペダル解放案内に応じて0.3秒間中立にします。これは案内を理解することを仮定した入力であり、人による理解度の検証ではありません。

試走結果、改善途中の失敗、21項目の修正、通常速度の追加確認は [レベル別試走レポート](../Artifacts/SkillPlaytest/README.md) を参照してください。保存済み最終6条件の不変条件は `python3 Tools/VerifySkillPlaytest.py Artifacts/SkillPlaytest/After` で検証できます。


## 高速・人間に近い入力での録画試走

`human-fast-items-7101` と `human-fast-control-7102` は、高速化後の通常2周を実車4台で走る。P1/P2は0.18/0.22秒ごとに車体位置・向き・速度・前方車両を観測し、最大360度/秒のハンドル操作と3/秒のペダル変化で入力する。操舵には小さな揺れを加える。コースを知っている運転であり、人の認知や楽しさを再現したものではない。前方100mの道路形状から制動を判断し、直線の目標は50/48m/s。車両の位置・速度は直接変えない。逸脱・停止が4秒続けば通常の復帰入力を送る。

```sh
RACING_BALANCE_SKILL_SUITE=1 \
RACING_BALANCE_RUN=human-fast-items-7101,human-fast-control-7102 \
RACING_BALANCE_VIDEO=1 \
RACING_BALANCE_OUTPUT="$PWD/Logs/HumanPlaytest/After" bash Tools/RunBalancePlaytest.sh
```

`RACING_BALANCE_VIDEO=1` は通常のP1カメラとHUDを1280×720・24fps・無音のMP4へ保存する。`RACING_FFMPEG` でffmpegのパスを指定できる。動画とゲーム時計を合わせるため `Time.captureFramerate=24`、時間倍率1に固定してオフライン収録する。動画は実際の物理走行だが、実時間のフレームレート測定ではない。開始準備は省略する。フレームと公式時計の対応は `video-timeline.csv` に保存する。大きい動画はLogs以下に置き、Gitへは集計と抜粋を保存する。
