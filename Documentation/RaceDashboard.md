# レース管理ダッシュボード

`Dashboard/index.html` は GitHub Pages に公開できる静的サイトです。ゲーム実行中の同じ PC で開くと、Unity の `RaceDashboardBridge` が `127.0.0.1:8765` で配信する状態・カメラ画像・シリアルログを読み取ります。ブラウザは USB シリアルポートを直接開きません。ESP32 のポートは引き続き Unity が占有します。

## 起動

1. Unity で `Assets/Scenes/SampleScene.unity` を開き、Play またはビルドを実行します。`Gmanager` がブリッジを自動起動します。
2. 同じ PC の Chrome または Edge で公開ページを開きます。ローカルで確認する場合は `Dashboard` ディレクトリを HTTP サーバーで配信します。
3. ブラウザが「ローカルネットワークへのアクセス」を求めた場合は許可します。別の PC からは接続できません。
4. `UNITY 接続中` が表示されたら、状態・2人のカメラ画像・シリアル入力を確認できます。

`http://127.0.0.1:8765/` をアドレスバーで開くと公開ダッシュボードへ移動します。ブリッジの稼働を直接確認する場合は `http://127.0.0.1:8765/api/status` を開くと JSON が表示されます。操作 API は許可されたダッシュボードからのみ受け付けます。

開始ボタンはタイトル画面、結果表示ボタンはレース中、再挑戦とタイトル復帰は結果画面で使えます。結果表示ボタンは `Gmanager.ShowResult()` を呼び、未完走プレイヤーは DNF として結果を確定します。ゲーム画面のプレビューは各表示のベースカメラを 640×360 JPEG として約2秒おきに取得します。GPU/CPU の負荷が増えるため、負荷が厳しい場合は画像更新間隔を `Dashboard/app.js` で調整してください。

シリアル監視は `InputManager` の処理済み行を最大80件保持します。キーボードデバッグモードではシリアルポートを使わないため、ポートは未接続と表示されます。`SampleScene` は現在キーボードデバッグモードなので、ESP32 を使う場合は `InputManager > Is Debug Mode` をオフにしてください。

## 接続と公開

通信は同じ PC のループバックアドレスだけで受け付け、CORS の許可元は `http://localhost`、`http://127.0.0.1`、`https://tk75attractions.github.io` に限定しています。GitHub Pages 自体はゲームの映像やシリアルデータを保存・中継しません。公開ページからローカルホストへの通信可否はブラウザのローカルネットワーク権限に依存します。Chrome/Edge を推奨します。

`.github/workflows/dashboard-pages.yml` は `main` に `Dashboard` の変更が入ると GitHub Pages に公開します。リポジトリの Pages 設定は **GitHub Actions** を選択してください。公開先は `https://tk75attractions.github.io/Racing/` です。
