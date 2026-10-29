# Editor起動時のUI重なり対策

2026-10-29 / Unity 6000.3.9f1。

## 確認した問題

SampleSceneでは `MainCanvas/OnPlay` と `MainCanvas/Result` が同時に有効で保存されていました。Resultには旧ゴール表示も含まれます。従来は `Gmanager.Awake` の機器入力・描画・UI構築などが完了した後でタイトルだけを表示していたため、その前に例外などで初期化が止まると、保存された複数の画面が表示されたままになります。

Windows EditorでPlay直後に重なるという報告に対する修正です。Mac Editorの通常起動では報告の症状は再現せず、Windows側の最初のConsole例外は未取得です。Windowsで初期化を停止させた要因の特定と、Windows実機での再確認は残っています。

## 修正

- 保存シーンのOnPlayとResultを非表示にする。
- `ScreenTransitionController.Awake` でタイトルだけを表示し、Gmanagerの初期化完了に依存させない。
- `Gmanager.Awake` でも機器入力の初期化前に表示状態を確定し、Awakeの呼び出し順に依存させない。
- UIのInitializeでも、フォント・演出などの構築より先に非表示状態を確定する。

## 検証

`ScreenStartupValidation` は保存シーンをPlayし、15 Editor更新後に表示状態と初期化時の例外を検査します。試験中の入力変更やGameObjectの無効化は保存しません。

- `ScreenStartupValidation.RunBatch`：成功。両者のタイトル起動、走行→ゴール→結果→タイトルの排他表示、P1切替時のP2独立性を確認。
- `ScreenStartupValidation.RunWithoutManagerBatch`：成功。走行・結果UIを有効に戻してGManagerを無効化し、ゲーム初期化が動かなくてもUI自身がタイトルのみへ戻すことを確認。
- 現行ランタイムソース全体をUnity付属RoslynとWindows Editor用の定義でコンパイル：成功。既存の警告のみ。
- `git diff --check`：成功。

実行例（実行中の同プロジェクトのUnityを閉じてから）：

```text
Unity -batchmode -projectPath <Racingの絶対パス> -executeMethod ScreenStartupValidation.RunBatch -logFile <ログの保存先>
Unity -batchmode -projectPath <Racingの絶対パス> -executeMethod ScreenStartupValidation.RunWithoutManagerBatch -logFile <ログの保存先>
```

Play移行と検証終了はスクリプトが制御するため `-quit` は付けません。WindowsではUnity.exeのフルパスを使用してください。

## mainへの切り出し

mainの `b21b790` から専用ブランチへ今回の変更だけを適用しました。未統合の夜景変更と既存のマテリアル変更は含みません。mainのCoreとランタイム全体をWindows Editor用定義で再コンパイルし、成功しました。

専用worktreeでのUnity Play再試験は、初回アセットインポート中のディスク容量不足で中断し、実行に至っていません。この再試験は成功扱いにしていません。今回生成したLibraryキャッシュを削除して空き容量を回復しました。上記のPlay検証は元の作業ツリーで実施した結果です。
