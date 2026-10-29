# 最新mainでビルドして起動する

ゲーム／Unity Editorの **Ctrl + Alt + U**（Macは **Cmd + Option + U**）、または管理画面の「ゲームの更新 → 最新mainでビルド・起動（Unityを終了）」を使います。Editorのメニューは `Racing > Update main, Build and Run` です。タイトル・結果・メンテナンス画面で実行できます。

1. 未保存のシーン、未コミット変更、進行中のGit操作、Unity Editorの場所を確認します。
2. `origin/main`を取得します。通信失敗やローカルmainの分岐・未pushコミットがあれば、ゲームを終了せず停止します。
3. チェック成功後に実行中のゲーム／Editorを終了し、`main`へ切り替えて `--ff-only` で更新します。変更の破棄や強制リセットは行いません。
4. 音声プラグインをビルドし、Unityのバッチビルドで `Builds/Operator/<日時>/Race.app`（Windowsは`Race.exe`）を作成して起動します。失敗時は以前のビルドを残します。ビルド用に生成した音声プラグインはパッケージへコピーしたあと元へ戻すため、次回の更新を未コミット変更で妨げません。

ビルド中は管理画面のUnity接続が切れ、新しいアプリが起動すると再接続します。詳細はプロジェクトの `Logs/OperatorUpdate/<要求ID>/update.log` と `build.log` に記録します。失敗した場合はログを確認し、原因を解消して以前のアプリ／Editorを起動してください。

## 必要なもの

- `main`ブランチと`origin`があるプロジェクトのGitクローン、Git、ネット接続・通常のGit認証。
- 対象バージョンのUnity Editorと有効なライセンス、対象OSのビルドモジュール。
- MacはXcode Command Line Tools、WindowsはVisual Studioのx64 C++ビルド環境（既存の`Native~/RaceAudio/build-windows.cmd`と同じ条件）。
- 管理画面からの操作は、既存のローカル／Tailscale操作権限が必要です。公開の閲覧画面からは実行できません。

`Builds`配下のアプリは親階層のプロジェクトを検出します。別の場所へ配置したアプリでは `RACING_PROJECT_PATH` にGitクローンの絶対パスを設定してください。Unityを標準以外の場所へ入れた場合は `RACING_UNITY_PATH` に実行ファイルの絶対パスを指定します。Macでは `.app/Contents/MacOS/Unity`、Windowsでは `Editor/Unity.exe` です。

この機能を含むアプリへの初回更新は通常の手順でビルドしてください。以降の更新からボタンとショートカットを利用できます。
