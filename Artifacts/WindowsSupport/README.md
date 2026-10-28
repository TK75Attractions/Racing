# Windows対応・操作マニュアル更新の検証

2026-10-27 / macOS / Unity 6000.3.9f1の参照アセンブリを使用。

- `sh Tools/Validate-Rgp.sh`：167 passed / 0 failed。既存NUnitテストにP2表示先の回帰テストを追加。2画面へのフォールバック、接続済み指定の保持、P1との重複防止、画面不足を確認。結果は `Artifacts/Rgp/test-results.xml`。
- Unity付属Roslynで現行のランタイムソース全体を `UNITY_STANDALONE_WIN` 定義でコンパイル：成功。Unity既存APIのobsolete警告と既存未使用フィールド警告のみ。
- 新規 `WindowsPlayerBuild.cs` をUnity Editor参照でコンパイル：成功。
- コンパイルした実際の `ValidateAudioPlugin` を呼び出し、DLL欠落、不正ヘッダー、同梱旧版DLLをそれぞれ `BuildFailedException` で拒否することを確認。旧版の不足APIは `ra_set_min_buffer_milliseconds` / `ra_get_underruns`。
- 手順書のローカルリンク・SVG XML：検証成功。
- HTMLをChromeで描画：5点の図・画像がすべて読み込み成功。1440px幅と390px幅で確認し、390pxでページ全体の横はみ出しなし。画像はHTMLへ埋め込み、閲覧にネット接続不要。

Windows Build Support、Windows実機、MSVCはこのMac環境にないため、Windows音声DLLの再生成、exeビルド・起動、物理2画面・COM入力・WASAPI音声は未実施。Windows上で `Tools/Build-Windows.ps1` を実行し、[操作マニュアル](../../Documentation/CustomerServiceManual.md) の動作確認を行う必要があります。同梱の旧DLLはこの更新では差し替えていません。

## 最新mainへの統合検証（2026-10-28）

- 統合元main: `5aa1e8473c83ae127b838e04e90837e7bc948e21`。メンテナンス画面とWindowsの表示先修正を両方保持。
- コア回帰テスト: 170 passed / 0 failed。Unity 6000.3.9f1 EditMode: 170 passed / 0 failed。
- `MultiDisplayValidation.Run`: 成功。Display 1/3の独立出力、異なる解像度、UI倍率、描画先の変更・解放を確認。物理モニターでの確認は未実施。
- 実際の `WindowsPlayerBuild.ValidateAudioPlugin` をUnity内から呼び出し、欠落・破損・同梱旧版DLLをすべて拒否することを再確認。
- `Tools/ExportOperationManual.mjs`: 再生成成功、コミット済みHTMLと一致。
- Windows上でのDLL再生成、exeビルド・起動、COM入力、WASAPI音声の実機確認は未実施。同梱旧DLLは変更していません。
