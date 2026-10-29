# メンテナンスBGM「橘諸兄」

2026-10-29。既存の `Audio/BGM/TachibanaMoroe` をP1/P2のメンテナンス専用音源としてループ再生する。

通常のBGM・効果音・エンジン音は停止したまま、専用音源だけAudioListenerの一時停止と独立USB/HDMI出力のメンテナンスミュートを通過する。全体・BGM音量は既存設定を使う。通常BGMのクリップ・再生位置・切替途中の状態を保持し、解除時には専用音源を止めて元のBGMへ戻す。専用音源は最初の使用時に生成し、繰り返しの切替で再利用する。

## 確認

- ランタイム全体の `dotnet build Assembly-CSharp.csproj --no-restore` 成功（既存の警告33件、エラー0件）。
- 新規 `MaintenanceMusicValidation.cs` をUnityの参照アセンブリと更新後のランタイムDLLに対してRoslynでコンパイル：成功。
- 管理画面UIとライブクライアントの回帰：48件成功。
- `git diff --check`：成功。

Unity Editorのライセンスクライアントが接続不能（IPC/mutexエラー）になっているため、今回のメンテナンスBGMのUnity実行検証とスピーカーでの聴感確認は未実施。`Racing > Validation > Maintenance Music` で両者への曲割当、通常音源と専用音源の停止条件、元の曲の保持、停止状態の復元を検査できる。成功時は `verification.txt` が出力される。

前回の壁カメラ・プレビュー・結果画面の実行検証は `Artifacts/OperationsFixes` に記録している。
