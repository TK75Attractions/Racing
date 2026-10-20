# 操舵アシスト・P2自動運転・CPU攻防・着地の調整記録

2026年10月20日。Unity 6000.3.9f1、実SampleScene、実際の学校モデル、コース長1904.19m。

## 実装

- 逸脱予測時に外向き入力を道路へ戻す操舵へ置換するアシスト。生のドリフト入力は維持する。
- P2自動運転とF8からのテスト開始。手動復帰はペダル解放を待ち、ポーズ／復帰ゲートを保つ。
- CPUの攻撃／抜き返しの時間、逆方向コースの初期レーンと退避方向、空いたレーンの選択を修正。
- 走行抵抗に必要なペダルを車両設定から計算し、速度目標へ届かない問題を修正。入力範囲は人間と同じ-1〜1。
- 踏切90m手前から間隔を取り、並走中は空中で中央へ合流しない。P2自動運転も横位置と車間を保つ。
- 着地判定を緩め、前後輪の接地が揃うまで最大0.9秒、小加速の条件成立を待つ。
- 次段階Gとして、完走／中断時の実際の成功を1件だけ結果へ表示。自動運転はテストの記録と表示する。

全機能をF8で比較できる。操作は [GameplayFeatures.md](../../Documentation/GameplayFeatures.md) を参照。

## 最終確認

`core-tests.txt`は97件成功。`unity-validation.txt`は実接地、アシストの無効条件、着地と個別OFF、P2入力切替と手動復帰、結果の保存／表示とON/OFF、静的日本語フォントを確認したログ。実際の車両抵抗と前輪の駆動力から必要ペダルを計算する式も検証した。

車の位置・向き・速度・回転・接地と前方の道路を観測し、反応間隔と操舵の揺らぎを持つ入力ボットで試走した。ハンドルとペダルを入力し、車体の位置・速度・周回・完走結果を強制設定していない。P2 AUTOの試走は製品側の自動運転と入力ゲートを使用した。全試走は3倍速。

| 条件 | 周回 | CPU→プレイヤーの追越 | プレイヤー→CPUの追越 | 人間の停止／復帰 |
|---|---:|---:|---:|---|
| 改善ON、両者22m/s設定、中級入力、アイテムなし | 2 | 21回 | 15回 | 最大停止0秒／復帰0回 |
| 改善ON、20／28m/s設定、中級／上級入力、アイテムあり | 2 | 9回 | 7回 | 最大停止0秒／復帰0回 |
| 改善OFF、20／28m/s設定、中級／上級入力、アイテムあり | 2 | 2回 | 0回 | 最大停止0秒／復帰0回 |
| P1に外向き入力、P2 AUTO、アイテムなし | 1 | 8回 | 7回 | 最大停止0秒／復帰0回 |

全条件でP1/P2が完走し、最終ON試走はCPUも全車完走した。CPUも実際のゴール判定で順位を得ており、人間の勝利を強制していない。試走エージェントが記録した期間の`runtimeErrors`は全条件で空。

追越は進捗差が±3mを越えて入れ替わり、世界位置40m以内、同じ高さ、前進中、路外フラグなし、復帰なしを条件とする。同じペアは5秒以内に重複集計しない。全順位変動を追越として数えた値ではない。集計は`Tools/AnalyzeRacingBattles.py`、詳細は`battle-on-analysis.json`、`battle-off-analysis.json`、`p2-stress-analysis.json`。

同じ乱数種と入力方針でも、順位によってアイテム抽選が変わり、旧CPUは起動からの時刻にも影響される。F8の全OFF→復元操作は最初の試走の5〜12秒に入る。2ケース連続のON試走では同速度ケースが先、速度差ケースが後で、後者は最初から最後まで改善ON。OFF側ではCPUのOFFは維持し、その他機能だけが同区間で比較切替される。各条件1回の観測であり、統計的な改善率・人の楽しさの証明ではない。

## 強い操舵補正とP2 AUTO

`p2-stress.json`では、P1の外向き入力にアシストが約1.60秒介入。`steering-stress.csv`にはゲーム入力値が+30前後のまま、実操舵が-9度前後へ切り返された例を保存した。車体の瞬間回転や横移動で戻してはいない。全車が完走し、製品と同じ走行範囲の定義で路外0秒、停止0秒、復帰0回。

P2は製品の自動運転で走り、P2側入力機器の切断・再接続も含めて継続した。手動へ戻す検証、ペダル解放、F8切替も成功。P1/P2とも指定ジャンプの着地加速1回。`p2-stress-skill-disconnect-rejoin-results.txt`は実レース後の表示で、P2は「自動運転テストの記録」。JSONの`p2TargetSpeed`／`p2Profile`は手動側の代替入力設定であり、P2 AUTO側は24m/sを目標とする。

## 誤検知と調整過程

旧試走エージェントは道路帯の幅だけで路外を測っていたため、合法なチューブ壁面走行を路外として数えていた。`battle-on.json`の速度差ケースに残るP1の2.14秒はこの旧定義による値。保存された22サンプルを実際のチューブCollider／走行範囲で監査し、すべてチューブ内、製品上の路外0サンプルだった（`tube-driving-area-audit.json`と`tube-band-samples.csv`）。元JSONは書き換えず残した。現在の測定はCarResetterと同じ走行範囲・チューブ内判定を使う。

調整中には、CPUが追いつく前に減速する、CPU同士が逆方向コースの初期グリッドで横切る、踏切で接触してP1を押す、CPUのペダル不足で着地が遅れる、P2が踏切で合流して失速する例を確認し修正した。最初の外向き入力テストがロケット中に重なったため、その条件を操舵補正の証拠には使わず、アイテムなしで再確認した。探索中の測定値は`iterations.json`へ残す。各版の実装と条件は同一ではなく、最終版の成績とは区別する。

## 結果表示と実行環境

`battle-on-*-results.txt`は実レース後の表示文字列。`result-720p.png`／`result-1080p.png`はUI検証用の固定データによる画像で、レイアウト・日本語・未完走・自動運転の区別を確認した。実機器の2画面・音声・入力遅延・初見者の理解・補正の感触は未評価。

Unity Editor起動時に検索インデックス内部の例外と未接続ESP32の初期化エラーがある。上記の実行エラー0は試走エージェントが記録した期間を指し、起動全体に例外がないという意味ではない。

製品変更は`05b19eb`（操舵・着地）、`34dacab`（P2／CPU）、`731993c`（結果表示）から段階的に追加した。最終の攻防試走は`731993c`にペダル・踏切修正を加えた作業ツリー。P2最終試走はさらに自動側の追従修正と測定を追加。最終製品ソースは`8a78eae`。主要ソースとシーンのSHA-256を`source-sha256.txt`に保存した。

## 再実行

```sh
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=skill-rivalry,skill-club-expert \
RACING_BALANCE_TIME_SCALE=3 RACING_FEATURE_PLAYTEST=1 \
RACING_BALANCE_OUTPUT=/private/tmp/racing-tuning-verified Tools/RunBalancePlaytest.sh

RACING_CPU_RACING=off RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=skill-club-expert \
RACING_BALANCE_TIME_SCALE=3 RACING_FEATURE_PLAYTEST=1 \
RACING_BALANCE_OUTPUT=/private/tmp/racing-tuning-final-legacy Tools/RunBalancePlaytest.sh

RACING_VALIDATE_FEATURES=1 RACING_P2_AUTO=1 RACING_ASSIST_STRESS=1 \
RACING_FEATURE_PRESET=experience RACING_BALANCE_SKILL_SUITE=1 \
RACING_BALANCE_RUN=skill-disconnect-rejoin RACING_FEATURE_PLAYTEST=1 \
RACING_BALANCE_TIME_SCALE=3 RACING_BALANCE_OUTPUT=/private/tmp/racing-tuning-p2-verified \
Tools/RunBalancePlaytest.sh

python3 Tools/AnalyzeRacingBattles.py /private/tmp/racing-tuning-verified
```

チューブの過去フラグ監査は、`RACING_GEOMETRY_AUDIT_TRACE`へCSVを指定し、Editorの`RaceExperienceGeometryAudit.Run`をバッチ実行する。生の全CSVとUnityログは上記の出力フォルダに保存される。
