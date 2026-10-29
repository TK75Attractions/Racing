# 最新mainでビルドして起動する

Unityを手動で起動しておく必要はありません。次のいずれかを使います。

- 管理画面の「ゲームの更新 → 最新mainでビルド・起動」。Unity停止中も常駐Dashboardから操作できます。
- Mac: `Tools/OperatorUpdate/Update and Run.command`をダブルクリック。Windows: 同じ場所の`Update and Run.cmd`。
- ゲーム／Unity Editor／管理画面を開いているとき: **Ctrl + Alt + U**（Macは **Cmd + Option + U**）。Editorのメニューは`Racing > Update main, Build and Run`です。管理画面ではブラウザのタブにフォーカスしてください。

レース中も更新できます。操作を受けるとメンテナンスへ切り替えます。更新に失敗した場合は、原因を解消して再実行するか、管理画面でメンテナンスを解除してください。Unity EditorはPlayしていない状態でも操作できます。ゲームだけを終了し、Editorは開いたままにします。Editorから実行した場合はPlayを停止して機器を解放し、未保存のシーンを保持します。

## 更新の流れ

1. `origin/main`を取得し、取得したコミットを固定します。通信・認証・Unityの場所・空き容量に問題があれば、ゲーム終了前にその理由を表示します。空き容量の目安はAssets容量の3倍と64MBです。
2. `Builds/OperatorSource/<要求ID>`に独立したGit worktreeを作ります。現在のブランチ、未コミット変更、未保存シーン、ローカルmainは変更しません。ローカルmainが先に進んでいても更新できます。
3. 別フォルダーで音声プラグインとUnityアプリをビルドします。
4. `Builds/Operator/<要求ID>/Race.app`（Windowsは`Race.exe`）を起動し、更新用worktreeを削除します。過去のアプリとログは保持します。

管理画面には取得・ビルド・起動の進捗とログの場所を表示します。ゲーム終了後の接続断でも更新ボタンの状態は常駐プログラムから取得します。設定値の変更番号が進んだだけでは更新要求を拒否しません。更新要求は現在のゲームセッションに対してだけ受け付けます。

ログは`Logs/OperatorUpdate/<要求ID>/status.txt`、`update.log`、`build.log`、取得したコミットは`commit.txt`です。更新の重複はプロジェクト共通の`Logs/OperatorUpdate.lock`で防止します。PCの強制終了でロックが残った場合は、更新プロセスが動いていないことを確認してから、このロックフォルダーだけを削除してください。

## 必要なもの・常駐Dashboardの更新

Gitクローン、GitとGit LFS、ネット接続とGit認証、対象バージョンのUnity Editorとライセンス・OSのビルドモジュールが必要です。MacはXcode Command Line Tools、WindowsはVisual Studioのx64 C++ビルド環境を使います。独立したUnityプロジェクトとアプリを生成するための空き容量も必要です。

標準以外の場所では`RACING_PROJECT_PATH`にGitクローンの絶対パス、`RACING_UNITY_PATH`にUnity実行ファイルを設定します。Macは`.app`のパスも受け付けます。Unity停止中は`ProjectSettings/ProjectVersion.txt`からバージョンを検出します。

既に常駐Dashboardを導入済みのMacでは、`Tools/DashboardRelay`で`dotnet run -- --update-service`を一度実行して補助プログラムも更新します。既存のTailscale経路・操作権限・通知登録は保持し、更新元プロジェクトの場所を記録します。新規導入は`Install Dashboard Service.command`です。公開の閲覧画面からは更新できません。

## 検証

`sh Tools/OperatorUpdate/test.sh`で関連する回帰検証をまとめて実行します（.NET 10 SDK・Mono・Python 3・Nodeが必要です）。

`Tests~/OperatorUpdate/test_update.py`は実Gitリポジトリを作成し、最新mainの取得、未コミット変更・作業ブランチ・ローカルmainの保持、独立したビルド元、Unity停止時の起動、通信・ビルド失敗、二重実行防止、後始末を検証します。Unityとアプリ起動だけを代替実行ファイルに置き換えます。

`HostTests.csproj`はUnity非接続時の更新、権限、同一IDの再送、進捗、再接続時の最新状態、レース中のメンテナンス切替、エラー理由を確認します。`RuntimeTests.cs`は本番の更新コンポーネントをEditor APIの代替実装で動かします。`test_bridge_update.py`は一時ポートで本番の更新要求ガードを検証し、実行中のゲームへ接続しません。管理画面のUI回帰テストは切断中の更新ボタンと専用APIへの送信も確認します。Windows用PowerShell実行はWindows実機で確認が必要です。
