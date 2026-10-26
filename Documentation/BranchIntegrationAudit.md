# 未統合ブランチの整理（2026-10-26）

監査基準は取得済みの `origin/main` = `237db2b`（PR #54統合後）。ローカル・originの74個のブランチ参照、同一先端をまとめた49組を比較した。35組・48参照はすでにmainの祖先であり、未統合の見かけだけが残っていた。全参照・SHA・先端日時・判断理由は[一覧JSON](../Artifacts/BranchIntegration/branch-inventory.json)に保存した。ブランチ・既存作業ツリーは削除していない。

## 今回の統合

| 対象 | 判断・取り込んだ内容 |
| --- | --- |
| `codex/physical-startup-diagnosis` / `6fa9283` | 6コミットをマージ。個別ハンドルの認識を待って校正し、校正後の角度基準の安定を待つ。復旧済みの起動警告を解決表示。診断ログの解析・JSON化を受信スレッドからワーカーへ分離し、滞留は上限と欠落通知を持たせる。ペダル解除待ちの原因と受信遅延を記録する。 |
| `codex/responsive-steering` / `ad1b408` | 未反映の実ハンドル90°校正と、空中重力検証に実際の物理経過時間を使う修正を選択移植。旧ヨー補助と古い速度補正は後発の高速旋回・ハンドル減速・SteeringStabilityに置換済みとして現在版を保持。検証成果物は旧結果を再利用せず今回実行する。 |
| `codex/display-audio-stability` / `d9b4160` | `git cherry 237db2b d9b4160`で追加パッチ0件。`31f3226`に既存の修正が取り込まれているため、コード変更なしで履歴を関連付けた。 |
| `codex/cabinet-mac-mount` / `eb3dc2f` | 10月19日版が `ca2e842` に存在。全ファイルで現行版を保持して旧保存ブランチを履歴上も整理。仮寸法の図面は再導入しない。 |
| `codex/neon-marker-material-settings` / `911bb7d` | 2色の小数丸めだけ（0.32→0.31999996など）。現行値を保持して履歴を関連付ける。 |

### 操舵統合の適応

10月18日当時にはなかった `LiveDriveInputSource`、`P1SteeringTestInputSource`、ダッシュボード入力リースにも校正値を引き継ぐ。実機→遠隔キー操作→実機復帰や入力機器の切替で、校正値が抜けたり二重適用されたりしないよう検証する。車両だけが角度を最大30の共通単位へ変換し、メニュー・RGP電文は元の単位を維持する。キーボード・CPU・自動運転の単位は維持する。

物理ハンドル90°で車両の最大入力、45°でその半分になる。速度別の切れ角上限、入力平滑化、旋回時減速、F8で切り替える横滑り対策は現在の実装を使用する。これは10月18日版の「安全上限内で曲率を比例配分する操舵方式」の全面導入ではない。高速度域では現在の切れ角上限へ先に到達する。既定ドリフト開始8単位は24°、最大チャージ30単位は90°相当になる。

実機診断側の競合は車種選択画面の1か所。現在のアーケード配置と接続案内用フォントを両方維持した。通信の安全タイムアウトとペダル解除条件は変更していない。

## 統合しない保存・旧試作

| ブランチ | 理由 |
| --- | --- |
| `codex/preserve-020f-worktree` | 切り離された作業状態の丸ごと保存。加減速・操舵HUDは現行に存在する一方、全面マージは安全修正・機能・学校マップ・検証成果物の削除を含む。復旧用として保持。 |
| `Engine-sound` | 9月〜10月初旬のRotary音声試作。現在のリアルエンジン音と独立音声出力を旧コードへ戻さない。 |
| `checkpoint` | 旧コース配置と初回ゴール処理。現在のスタート位置に応じた通過判定と本番コースを維持。 |
| `temp` | 9月の難易度選択・タイトル・リザルト試作。現行のプリセットとプレイヤー別準備・アーケードUIで置換済み。 |
| `mud/testcourse` | 7月のコース・ガラス・NavMesh試作。現在の本番コースに戻さない。 |
| `ODA_` | 共通祖先のない旧校舎・モデル作業。現在の学校マップを巻き戻す統合は行わない。 |
| `origin/ガタガタを治す` | 旧カメラ振動の値調整。現在は走行カメラ振動が既定OFF。 |
| `raymee/night-ennichi-visuals` / `codex/night-ennichi-arcade-ui` | 別依頼の夜景・新UI検証。検証済み統合版は `2f6264f`。今回のmain整理に混ぜず保持。 |

## 監査開始時点ですでに統合済み

- `backup/main-before-sync-20261018-afac944`, `codex/skill-playtest-improvements`
- `codex/arcade-sport-ui`
- `codex/autonomous-race-polish`
- `codex/backup-dashboard-before-split-7f1ff39`
- `codex/bugfix`
- `codex/car-selection`
- `codex/course-safety-playtests`
- `codex/dashboard-f8-settings`
- `codex/dashboard-fps`
- `codex/dashboard-live-settings`
- `codex/dashboard-player-input`
- `codex/fix-course-crest-collisions`
- `codex/fix-wall-bounce-speed`
- `codex/generate-race-course-road`
- `codex/ignore-course-height`
- `codex/integrate-pending-changes`
- `codex/integrate-rendering-audio`
- `codex/npc-race-participants`
- `codex/playthrough-flow-fixes`
- `codex/race-sound-effects`
- `codex/racing-finish-and-control-fixes`
- `codex/racing-gameplay-features`
- `codex/racing-polish`
- `codex/realistic-engine-audio`
- `codex/rendering-and-audio-optimizations`
- `codex/rgp-serial-monitor`
- `codex/slope-camera-tube-driving`
- `codex/smooth-race-course-road`
- `codex/stable-driving-and-player-minimap`
- `codex/steering-comfort`
- `codex/tube-course-road`
- `feat/dashboard-device-controls`
- `main`
- `origin/main`
- `origin/raymee/visual-quality`

## 検証

今回の結果は[検証フォルダー](../Artifacts/BranchIntegration)に保存する。検証は専用作業ツリーで行い、元プロジェクトの開いているUnityエディターと未保存シーンには触れない。実機ハンドル・USB・2台のディスプレイでの最終評価は今回の自動検証には含めない。

| 検証 | 結果 |
| --- | --- |
| Unity EditMode / Mono Core | 同じ166テストが両環境で成功（失敗0） |
| ダッシュボード表示・診断 | 54件成功 |
| Relay / WebSocket・起動・権限・復旧 | 29件成功、実PTY拡張1件は既定OFFでスキップ |
| 通知暗号・永続化・復旧判定 | ローカル捕捉HTTPで31チェック成功 |
| 実機角度校正の受け渡し | 実機→P1単体→ダッシュボード→live入力→車両、キーボード切替・リース終了を成功 |
| 高速旋回 / 急操舵 | 実PrefabとPhysXで31条件 / 86条件成功 |
| Play Mode安定性 | 着地・空中・旋回・ドリフトなど58条件成功 |
| 車種選択 | P2先行、独立選択・練習、確認解除待ち、レース開始まで成功 |
| 対戦フロー | 加速、ゴール、観戦、実リプレイ、リザルト、再開、タイトル復帰、棄権・タイムアウト成功 |

既存の8765/8766では稼働中のサービスと衝突したため、通信連携の最終結果はソースの一時コピーの固定ポートだけを18765/18766へ変更して取得した。判定や実装ロジックは変更していない。校正のアダプター経路は元の統合作業ツリーでもUnity/Monoで成功している。

Unityの起動直後に既存の `UnityEditor.Search.SearchDatabase` インデックス例外が出るが、コンパイルと全試験は完了した。実機のUSB・IMUと人が感じるハンドル感度、2画面・独立音声機器での検証は未実施。端末上で開かれた元プロジェクトは変更せず、稼働中のサービスも停止していない。

物理試験の再現は `-batchmode -nographics -executeMethod BranchIntegrationValidation.RunPhysics -quit`。Play Modeの外部公開停止には専用プロジェクトで `RACING_BRANCH_INTEGRATION_VALIDATION=1` を設定する。操舵の58条件は `CarStabilityValidation.RunFullBatch`、画面遷移は `CarSelectionPlayModeValidation.Run` と `MultiplayerPlayModeValidation.RunBatch` を使い、非同期試験には `-quit` を付けない。
