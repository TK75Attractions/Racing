# 音源・音楽同期・スタート配置・運用更新

2026-10-29 / Unity 6000.3.9f1 / macOS。

- オープニングを`Race_Opening_v3`、ゴールを`finishv4`、結果を`result_v3`へ更新。ゴールと結果は別の曲に分離。
- 同じ曲は共通のDSP時刻に合わせて予約再生。遅れて同じ曲へ移る画面は、ループを含め同じ再生位置に合流する。両者が曲を離れたら次回の再生時刻をリセットする。独立した効果音・エンジン音の経路は維持。
- グリッド生成時に実際のタイヤ球形コライダーと静的路面の距離で接地させる。CPUはイントロ／カウントダウン中に物理演算が固定されるため、固定する前に位置を確定する。CPUの列を14m後方に配置し、人間の追従カメラにCPU車体が重ならないようにする。
- 管理画面、ゲームのCtrl/Cmd + Alt + U、Editorメニューから外部更新処理へ引き継ぐ。mainの取得、終了待ち、fast-forward、音声ビルド、Unityビルド、新しいアプリの起動まで実装。使用手順は[OperatorUpdate](../../Documentation/OperatorUpdate.md)。

## 確認

- Unityの`PlayerAudioValidation`、`AudioBufferingValidation`、`MaintenanceMusicValidation`成功。
- `OperatorChangesValidation`のPlay検証成功。4台が生成直後に接地し、P1/P2のレンズがCPUコライダーに入らないこと、両画面の実描画にコースの画素があることを検査。
- `start-p1.jpg`、`start-p2.jpg`を取得し、コースとカウントダウンが表示されることを目視確認。
- 管理画面UI・ライブクライアント50テスト成功。更新ボタンの権限・接続・実行中条件、引数なしの更新命令を含む。
- 外部更新処理7テスト成功。実際の一時Gitリポジトリとローカルoriginを使い、fast-forwardとビルド・起動呼び出し、未コミット変更、ローカルmain先行、Editor欠落、取得失敗、ビルド失敗、チェックのみの実行を検査。Unity／OS起動コマンドはこの7テストでは代替する。
- Unityから本番の外部更新プロセスを実行し、未コミット変更で停止して終了許可マーカーを作らないことを確認（`updater-preflight.txt`）。
- `OperatorPlayerBuild.Build`でMacリリースビルド成功。生成アプリの起動後、`/api/status`が`Title`・`updateAvailable=true`を返すことを確認（`mac-build-startup.txt`）。起動例外なし。
- 既存中継全体の回帰スイートは常駐中継が使用中の8766番ポートと競合し完走せず。Web Push 31チェックと入力ハーネスは成功。上記50件のUI・ライブクライアント検証とは別の結果。
- ランタイム全体の`dotnet build Assembly-CSharp.csproj --no-restore`成功（既存警告33件、エラー0件）。

USB/HDMI実スピーカーでの聴感、機器固有の出力遅延、Windowsのビルドと起動は未確認。曲の共通再生位置の検証と、機器ごとの物理的な遅延の測定は別です。
