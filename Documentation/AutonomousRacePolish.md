# 自律改善・検証レポート（2026-10-25）

既存の Arcade Sport UI（`6eb6360`、P1青／P2ピンク）を維持し、練習・走行案内・復帰操作を改善した。作業ブランチは `codex/autonomous-race-polish`。実行中の別のUnityと、元のチェックアウトの運営マニュアルの未コミット変更を保持するため、専用worktreeで作業した。

## 修正と効果

- **練習中の復帰ボタン**：押し続けた間の毎フレームの復帰を、押した瞬間の1回へ変更。離して押し直す操作は引き続き有効。
- **練習完了音**：アイテム接触が更新フレーム間に起きると完了音が出なかった。前回通知した段階を保持し、取得完了を各プレイヤーへ1回通知する。
- **ジャンプ案内**：相手接近・アイテム効果などの静かな通知との競合を解消。「この先ジャンプ：まっすぐ加速」を独立した優先案内へ戻した。接続・復帰・ペダル解放の案内を最優先とし、ジャンプは通常の操作ヒントより優先する。
- **案内の安定性**：通常ヒントは最低0.8秒読めるようにし、重要案内は直ちに割り込む。接続復旧やペダル解放が済んだ指示はすぐ消し、終了・再挑戦時の表示残りも防ぐ。夜景ブランチの通知キューを、Unityの時計に依存しないCoreへ移して回帰テストを追加した。
- **練習画面**：中央カードをロゴとタイマーの間へ移し、道路と車体を隠さない配置にした。「この先は右カーブ／道にそって曲がろう」と予告し、直線ですぐに右へ切る誤解を避ける。課題の達成・実行中・未達成を3本のバーで区別する。
- **待機・接続案内**：練習完了・時間切れでは「もう一人の準備待ち／そろったらレースへ」。接続待ちや結果からの個別復帰も日本語で案内する。接続状態が変わっても課題番号が同じだと見出しが更新されない問題も修正した。
- **自動運転表示**：P1でもP2と出ていた固定文言を、表示中の車両の番号へ修正。観戦表示でも車両の番号に従う。
- **文字の表示**：変更文言を含む369文字で静的日本語フォントを再生成。実行時の追加生成に頼らず、720p／1080pで欠字・文字のはみ出しを検査した。

## 夜景ブランチからの選別

`origin/raymee/night-ennichi-visuals` を作業開始・修正の区切り・終了前に取得して確認する。確認した先端は `c0d6158add4a7a1f229d0430f90e0f4fdce08af0`。

取り込んだバリア描画は以下の3ファイルに限定した。

- `RaceBarrierPath.cs`：描画メッシュのUV2へ、上下の縁までの距離を追加。
- `SG_RaceBarrier.shader`：その距離から細い発光縁を描画。追加で、UV2を持たない既存メッシュ全体が発光しないガードを設けた。
- `M_RaceBarrier.mat`：六角格子と面の主張を弱め、境界の発光を使う設定。

描画頂点の位置、衝突メッシュ、道路形状、レース用の設定は変更していない。昼の同じ画角で比較し、夜も実シーンの照明を一時的に切り替えて確認した。景観・固い支柱・反射・路面の全面変更・描画パイプラインの変更は、関連アセットと負荷の影響が大きいため、このPRでは採用していない。参照ブランチ自体は変更していない。

## 調査と再改善

1. 変更前のEditModeテスト152件を実行し、全件成功を確認した。
2. 最初の高速試走は両者完走したが、古い結果UI向けの検証条件で後続試走が停止した。コンパクトな結果UIでは実績のスナップショットAPIを表示設定にかかわらず保持するため、検査を現在の契約へ修正した。旧「停車課題」を見るUI検査も、現在の「実際のアイテム取得」へ追従させた。
3. 独立した試走が実機シリアルや管理画面のサーバーを開かないよう、エディター用の試走入口でテスト入力と外部公開停止を設定した。製品の入力選択は変更していない。
4. 復帰ボタンと完了音を修正し、実車の練習走行で検証した。
5. 改善前の3条件を測定。初心者モデルは最初のジャンプで復帰を繰り返し、360秒で未完走だった。表示中のジャンプ案内が静かな通知へ移された一方、試走エージェントは主案内だけを読んでいた不整合もあった。
6. 安全案内の優先表示とレイアウトを改善し、昼夜の描画・文字・衝突保護・入力ゲートを検証した。画像を実際に開いて、撮影用固定背景がUIを隠す検証用の重なり順も修正した。
7. 実画面を見て、練習の旋回案内を「今すぐ右へ」ではなく「この先の右カーブ」の予告へ再調整し、最終文言で再検証した。
8. 同じ3条件と追加のアイテムあり条件を、変更後のコミットで再試走した。結果は成果物フォルダーの比較表とJSONに記録する。

## 検証

Unity **6000.3.9f1**。同一プロジェクトを同時起動せず、独立worktreeで順番に実行した。

| 検証 | 結果 |
| --- | --- |
| EditMode | 変更前152件／変更後159件、全件成功。追加7件は通知の割込み・表示保持・更新・解除・期限・重複・リセット |
| UI・文字 | P1／P2、720p／1080p、接続／再接続／待機／練習段階、欠字とはみ出し、入力ゲート、実績・DNF・終了ゲートを確認 |
| 既存ゲームルール | `SkillGameplayValidation.Run` と `RacingPolishValidation.Run` 成功 |
| コース・バリア | 屋外1,522地点で両側の衝突面を確認。欠落0、チューブ内の余分な壁0。4条件の車体サイズ物理試験も成功 |
| 練習の実走 | 両者が加速→旋回→アイテム接触→レース開始を完了。復帰の押し続け／押し直しと、完了音各1回を検査 |
| 車選択 | P2先行、ペダル長押し、他方の練習との並行、練習タイムアウト、レース開始を確認 |
| 終了画面 | P1／P2の個別終了、踏みっぱなし防止、独立したタイトル復帰を確認 |

試走の比較値、画像、テストの成功マーカーは [成果物の索引](../Artifacts/AutonomousRacePolish/README.md) を参照。

## 再実行

```sh
PROJECT="$PWD"
OUTPUT="$PWD/Logs/AutonomousPolish/Recheck"
mkdir -p "$OUTPUT"
Unity -batchmode -nographics -projectPath "$PROJECT" -runTests -testPlatform EditMode -testResults "$OUTPUT/editmode.xml" -logFile "$OUTPUT/editmode.log"
RACING_POLISH_OUTPUT="$OUTPUT/Visuals" Unity -batchmode -projectPath "$PROJECT" -executeMethod AutonomousPolishUIValidation.Run -quit -logFile "$OUTPUT/ui.log"
Unity -batchmode -projectPath "$PROJECT" -executeMethod AutonomousPolishUIValidation.RunRules -quit -logFile "$OUTPUT/rules.log"
RACING_TUTORIAL_QA_OUTPUT="$OUTPUT/Tutorial" Unity -batchmode -projectPath "$PROJECT" -executeMethod DrivingTutorialValidation.Run -logFile "$OUTPUT/tutorial.log"
Unity -batchmode -projectPath "$PROJECT" -executeMethod CarSelectionPlayModeValidation.Run -logFile "$OUTPUT/selection.log"
Unity -batchmode -projectPath "$PROJECT" -executeMethod ArcadeEndingPlayModeValidation.Run -logFile "$OUTPUT/ending.log"
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_TIME_SCALE=4 \
RACING_BALANCE_RUN=human-fast-items-7101,skill-fast-pace,skill-novice-expert,skill-disconnect-rejoin \
RACING_BALANCE_OUTPUT="$OUTPUT/Drive" bash Tools/RunBalancePlaytest.sh
```

`Unity` は環境の実行ファイルへ置き換える。Play Modeの検証と試走は自分でEditorを終了するため `-quit` を付けない。大きい生トレースとログは `Logs/AutonomousPolish/` に保持し、Gitには画像・集計・検証マーカーを保存した。

## 限界と残課題

- コースを知る入力エージェントによる比較であり、初見の小学生の理解度や楽しさの測定ではない。特に初心者モデルは表示された案内を0.35秒後に理解して加速する仮定なので、改善前後の値には案内の接続修正の影響も含む。運転の腕前そのものの改善率として扱わない。
- 低速の初心者モデルは復帰43回から0回、走行進捗171mから3,407mへ進んだが、360秒の制限では未完走が残る。実際の初心者の操作を確認し、展示の回転時間と合わせて難易度を評価する余地がある。
- 乱数seedが同じでもUnityの物理とCPUの展開は完全には一致しない。時間倍率4の試走から実時間のフレームレートを推定しない。
- 測定順は変更前が高速→初心者→接続断、変更後がアイテムあり→高速→初心者→接続断。CPUには累積の `Time.time` の影響もあるので、比較値を厳密な因果効果や改善率として扱わない。
- 練習の個別ルール検査では姿勢・速度を設定して境界条件を作る。その後の練習走行は本番入力とアイテム接触で課題を達成する。バランス試走は車両姿勢・速度・周回・結果を強制設定しない。
- 実ESP32・ハンドル・ペダル・外部モニター・独立音声機器、Windows実機、4KでのGPU負荷と長時間運用は今回未検証。
- 独立したバッチEditorの起動時にUnity内蔵Searchの `SearchDatabase` が `ArgumentOutOfRangeException` を出す環境由来の問題が残る。ゲームの検証はその後に完了しているが、Editorログが無警告であるとはしていない。
- PRは既存UIの `6eb6360` を土台にするため、`main`との差分には先行するArcade Sport UIのコミットも含まれる。今回の追加改善は `546a5dd` 以降。`main`へのpush・マージは行っていない。
