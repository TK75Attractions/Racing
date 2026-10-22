# タイトル・結果の校舎背景

実装commit: `aa1c78f`、ダッシュボード統合: `90e7665`。Unity 6000.3.9f1の実際のSampleSceneをPlay Modeで動かして検証。

- `title.jpg`: 校舎全景を見渡すタイトル画面。
- `result.jpg` / `result-p2.jpg`: 各プレイヤーの結果画面。表示タイムはDebugPreviewResultのプレビュー用データで、走行計測の結果ではない。
- `returned-title.jpg`: P1だけタイトルへ戻り、P2は結果画面に残っている状態。
- `validation.txt`: メニューカメラ・F8 OFF/ON・コンポーネントの無効化/再有効化・個別のタイトル復帰・紹介/カウントダウン/走行カメラへの復帰の検証結果。
- `campus-checks.json`: 共通の俯瞰用シャドウ設定を50m/2048から650m/4096へ上げ、走行時に50m/2048へ復元した記録。当たり判定の保持も確認。

ハードウェア不要のため、この検証内の保存しないシーンだけ入力をキーボードへ切り替え、音声出力と外部公開を停止した。録画やレースタイムの測定ではない。Editorの既知のSearch起動例外は判定から除外し、それ以外の実行時Error/Exceptionは検証失敗として扱う。

再実行手順は `Documentation/MenuScenery.md` を参照。

## ダッシュボード統合後の確認

mainの新しい共有デバッグカタログにもMenuSceneryを追加し、F8/管理画面の24番目の機能として同期。統合後に上記Play Modeの検証と撮影を再実行した。136件のCoreテスト、37件のDashboard UIテストが成功。

通常ポート8765/8766に既存サービスが稼働していたため、最初のDashboardRelayテストはポート競合で失敗した。元のサービスを停止せず、検証用コピー内で両ポートの参照だけを18765/18766へ置き換えて再実行し、27件成功・0件失敗・PTY実機テスト1件省略。`dashboard-tests.txt` と `dashboard-integration.json` はこの分離した検証の記録。Coreテストの記録は `core-test-results.xml`。
