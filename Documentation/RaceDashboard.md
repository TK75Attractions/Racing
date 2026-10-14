# レース管理ダッシュボード

`Assets/StreamingAssets/Dashboard/index.html` は Unity ビルドに同梱される画面で、同じファイルを GitHub Pages にも公開します。Unity の `RaceDashboardBridge` が `127.0.0.1:8765` で画面・状態・カメラ画像・シリアルログを配信します。ブラウザは USB シリアルポートを直接開きません。ESP32 のポートは引き続き Unity が占有します。

## 起動

1. Unity で `Assets/Scenes/SampleScene.unity` を開き、Play またはビルドを実行します。`Gmanager` がブリッジを自動起動します。
2. 同じ PC のブラウザで `http://127.0.0.1:8765/` を開きます。画面と API は同じ Unity プロセスから配信されます。
3. GitHub Pages の画面は、Firebase を設定すると別端末・外出先から状態監視に使えます。操作担当 UID には進行コントロールも表示されます。Firebase 設定が空の場合は設定待ちを表示します。
4. `UNITY 接続中` が表示されたら、状態・2人のカメラ画像・シリアル入力・人間とCPUの参加車両を確認できます。

`http://127.0.0.1:8765/` をアドレスバーで開くとローカルのダッシュボードを表示します。`http://127.0.0.1:8765/api/status` を開くと JSON で状態を確認できます。操作 API は許可されたダッシュボードからのみ受け付けます。

開始ボタンはタイトル画面、結果表示ボタンはレース中、再挑戦とタイトル復帰は結果画面で使えます。結果表示ボタンは `Gmanager.ShowResult()` を呼び、未完走プレイヤーは DNF として結果を確定します。ゲーム画面のプレビューは各表示のベースカメラを 640×360 JPEG として約2秒おきに取得します。GPU/CPU の負荷が増えるため、負荷が厳しい場合は `Assets/StreamingAssets/Dashboard/app.js` の画像更新間隔を調整してください。

シリアル監視は `InputManager` の処理済み行を最大80件保持します。キーボードデバッグモードではシリアルポートを使わないため、ポートは未接続と表示されます。`SampleScene` は現在キーボードデバッグモードなので、ESP32 を使う場合は `InputManager > Is Debug Mode` をオフにしてください。

## 接続と公開

ローカル画面は同じ PC のループバックアドレスで配信します。GitHub Pages 自体はゲームデータを保存しません。Firebase 設定後は、Unity が HTTPS で Realtime Database の `/live` を更新し、公開画面は認証後にその値を閲覧します。操作担当 UID は `/control/request` に命令を送り、Unity が状態を確認して実行し、`/control/ack` へ結果を返します。映像はローカル画面のみです。

`.github/workflows/dashboard-pages.yml` は `main` に `Assets/StreamingAssets/Dashboard` の変更が入ると、同じファイルを GitHub Pages に公開します。公開先は `https://tk75attractions.github.io/Racing/` です。Firebase の接続は [具体的な設定手順](FirebaseSetup.md)、構成の根拠は [遠隔監視の設計](RemoteRaceDashboardPlan.md) を参照してください。

## NPCの状態監視

「参加車両・CPU状態」はP1/P2/CPU 1/CPU 2を順位順に表示します。順位、確定済み周回数、実速度、ゴール基準の累積進行距離を共有のレース参加車両から読み取ります。CPUには目標速度、追走相手（P1/P2）、相手との進行距離差、安全復帰回数、接触保護も表示します。差が＋なら相手が前、−なら相手が後ろです。距離差は周回込みで、最短の物理距離ではありません。

CPUの状態は停止／走行中／空中／後退復旧／接触後の譲り／停止・復旧待ち／完走／未完走です。安全復帰の回数は逆走操作や接触保護の回数を含まず、実際に安全確認後の位置復帰が成功した回数です。完走・未完走時は確定順位・完了周回数を表示し、CPUの完走だけでは人間のレースを終了させません。次の組では参加車両と復帰回数を作り直し、接続を失った画面では以前の行を消します。

ローカルでは既存の `/api/status` を1秒ごとに取得し、遠隔では既存のFirebase送信間隔（5秒）で更新します。追加カメラや画像取得は行いません。`players` は2人の入力用のままで、別の `participantCount` と `participants`（最大4台）で参加車両を送ります。遠隔のスキーマバージョンは1を維持し、旧Unityのデータも受け付けます。4KiBを超える場合はシリアル行を減らし、参加車両を残します。

遠隔へ反映する順序は、`Firebase/database.rules.json`の適用 → 管理サイトの配信 → 更新したUnityビルドの起動です。古いRulesは参加車両フィールドを拒否するため、先に更新してください。今回のコミットでは本番RulesやGitHub Pagesへの公開は行っていません。

検証はUnityの `Racing > Validate NPC Dashboard`（バッチは `-batchmode -nographics -executeMethod NpcDashboardValidation.Run -quit`）、画面の自動テストは `node --test Tests~/Dashboard/dashboard-ui.test.cjs`、Firebaseルールは従来通り `Firebase/tests` の `npm test` を使います。今回、Unityのコンパイルと状態・終了・リセット・Unicode込み4KiB制限、画面の4テスト、Database Emulatorの権限・CPUフィールド・異常値拒否・従来データ互換性を確認しました。実際の本番Firebaseへのアップロードは未実施です。
