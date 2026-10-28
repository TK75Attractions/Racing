# Windows対応・操作マニュアル更新の検証

2026-10-27 / macOS / Unity 6000.3.9f1の参照アセンブリを使用。

- `sh Tools/Validate-Rgp.sh`：167 passed / 0 failed。既存NUnitテストにP2表示先の回帰テストを追加。2画面へのフォールバック、接続済み指定の保持、P1との重複防止、画面不足を確認。結果は `Artifacts/Rgp/test-results.xml`。
- Unity付属Roslynで現行のランタイムソース全体を `UNITY_STANDALONE_WIN` 定義でコンパイル：成功。Unity既存APIのobsolete警告と既存未使用フィールド警告のみ。
- 新規 `WindowsPlayerBuild.cs` をUnity Editor参照でコンパイル：成功。
- コンパイルした実際の `ValidateAudioPlugin` を呼び出し、DLL欠落、不正ヘッダー、同梱旧版DLLをそれぞれ `BuildFailedException` で拒否することを確認。旧版の不足APIは `ra_set_min_buffer_milliseconds` / `ra_get_underruns`。
- 手順書のローカルリンク・SVG XML：検証成功。
- HTMLをChromeで描画：5点の図・画像がすべて読み込み成功。1440px幅と390px幅で確認し、390pxでページ全体の横はみ出しなし。画像はHTMLへ埋め込み、閲覧にネット接続不要。

Windows Build Support、Windows実機、MSVCはこのMac環境にないため、Windows音声DLLの再生成、exeビルド・起動、物理2画面・COM入力・WASAPI音声は未実施。Windows上で `Tools/Build-Windows.ps1` を実行し、[操作マニュアル](../../Documentation/CustomerServiceManual.md) の動作確認を行う必要があります。同梱の旧DLLはこの更新では差し替えていません。
