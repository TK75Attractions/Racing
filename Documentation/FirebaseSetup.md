# Firebase 遠隔監視の接続手順

この手順を終えると、Unity を動かす PC から Firebase Realtime Database にレース状態を送り、別端末から [公開ダッシュボード](https://tk75attractions.github.io/Racing/)で閲覧できます。**操作担当者**は公開画面から進行を操作できます。プレイヤー映像は [ローカル画面](http://127.0.0.1:8765/)だけに表示します。

## 1. Firebase プロジェクトを作る

1. [Firebase コンソール](https://console.firebase.google.com/)で新規プロジェクトを作成し、**Spark（無料）**プランを選ぶ。Google Analytics はこの機能には不要。
2. プロジェクト内で **Realtime Database** を作成する。近い地域を選び、最初は**ロックモード**にする。画面に表示される `https://...firebasedatabase.app` または `https://...firebaseio.com` 形式の **database URL** を控える。Firestore とは別のサービスなので取り違えない。
3. **Authentication → Sign-in method** で **メール／パスワード**を有効にする。**Users** で Unity 専用の書込ユーザー 1 件と、運営者の閲覧ユーザーを作る。遠隔操作させるユーザーの UID も控える。操作担当者は閲覧ユーザーの一部として登録する。各アカウントには別のメールアドレスを使う。
4. プロジェクト設定で Web アプリを登録し、表示される `firebaseConfig` の `apiKey`、`authDomain`、`databaseURL`、`projectId`、`appId` を控える。API key は公開設定値であり、書込パスワードではない。[Web アプリ登録の公式手順](https://firebase.google.com/docs/web/setup)。

## 2. データベースの権限を設定する

1. Realtime Database の **Data** 画面で、ルートに次の管理用データを作る。UID は Authentication の Users 画面から正確にコピーする。`roles` はクライアントから読み書きできない設定にする。

   ```json
   {
     "roles": {
      "writerUid": "ここにUnity書込ユーザーのUID",
      "viewers": {
        "ここに閲覧ユーザーのUID": true,
        "ここに操作担当ユーザーのUID": true
      },
      "operators": {
        "ここに操作担当ユーザーのUID": true
      }
     }
   }
   ```

   同じ UID が閲覧と操作を兼ねる場合は `viewers` と `operators` の両方に登録する。閲覧を停止する場合は両方からその UID を削除する。
2. Realtime Database の **Rules** 画面へ [database.rules.json](../Firebase/database.rules.json) の内容を貼り、公開する。`/live` を読み取れるのは `roles/viewers` にある UID だけ、書けるのは Unity の書込 UID だけ。`/control/request` は操作担当 UID だけが書け、Unity の書込 UID だけが結果を `/control/ack` に書く。全体の公開読み取りや公開書き込みは許可しない。
3. Rules Playground または [Local Emulator Suite](https://firebase.google.com/docs/emulator-suite) で、匿名・未登録 UID が読めないこと、閲覧 UID が操作命令を書けないこと、操作担当 UID が現在の状態に合う命令だけを書けることを確認する。Rules の適用前に公開サイトを本番接続しない。

開発時の自動テストは `Firebase/tests` で `npm install`、続いて `npm test` を実行する。Java と Node.js が必要。実際の Firebase プロジェクトには接続せず、Database Emulator だけで権限と状態の判定を試す。

## 3. 公開設定を入れる

[firebase-config.json](../Assets/StreamingAssets/Dashboard/firebase-config.json) の空欄を、手順 1 で控えた Web アプリ設定に置き換える。ここには**パスワード、ID token、サービスアカウント鍵を入れない**。このファイルは Unity ビルドと GitHub Pages の両方に配られる。

```json
{
  "apiKey": "AIza...",
  "authDomain": "YOUR_PROJECT.firebaseapp.com",
  "databaseURL": "https://YOUR_DATABASE.REGION.firebasedatabase.app",
  "projectId": "YOUR_PROJECT",
  "appId": "1:...:web:..."
}
```

変更を GitHub の `main` に反映すると、[dashboard-pages.yml](../.github/workflows/dashboard-pages.yml) が同じ画面と設定ファイルを GitHub Pages に公開する。値が空の間は、公開画面に「Firebase プロジェクトの設定待ち」と表示される。

## 4. Unity PC に書込用の資格情報を置く

[writer-config.example.json](../Firebase/writer-config.example.json)を参考に、次の JSON を **Unity が表示する `Application.persistentDataPath` 内の `race-dashboard-writer.json`** として作る。Firebase の書込ユーザーのメールアドレスとパスワードを入れる。

```json
{
  "email": "writer@example.com",
  "password": "書込ユーザーのパスワード"
}
```

このファイルは Git の管理対象外に置き、他の OS ユーザーが読めない権限にする。macOS/Linux ではファイルに `chmod 600` を設定する。Windows ではファイルの「プロパティ → セキュリティ」で実行ユーザーだけに読み取りを許す。`Application.persistentDataPath` の場所は OS・Unity の実行方法によって変わる。公開設定を入れた後に書込ファイルが見つからなければ、Unity Console に**必要な絶対パス**が警告として表示される。現在のプロジェクト設定は `DefaultCompany` / `Race` だが、表示されたパスを優先する。

Unity の `SampleScene` を Play するか、更新したビルドを起動する。[RaceRemoteFirebasePublisher.cs](../Assets/Managers/RaceRemoteFirebasePublisher.cs)が自動で追加され、書込アカウントでログインして 5 秒ごとに `/live` を上書きする。Firebase Console の Data 画面で `/live/updatedAt` と `/live/seq` が変わることを確認する。`updatedAt` は Firebase のサーバー時刻（ミリ秒）なので、Unity PC の時計とは独立している。Unity が止まれば値は残るが、公開画面は更新停止とオフラインを表示する。

## 5. 別端末から確認する

1. Unity PC と**別のネットワーク**のスマートフォンなどで [公開ダッシュボード](https://tk75attractions.github.io/Racing/)を開く。
2. 手順 1 の閲覧ユーザーでサインインする。公開画面は Firebase Web SDK から `/live` の変更通知を受ける。閲覧アカウントはタブを閉じるとセッションが消え、画面の「ログアウト」からも終了できる。
3. Unity の状態、P1/P2 の数値、ポート状態、直近のシリアル行が見えることを確認する。画像は遠隔画面に表示されない。フロー図は現在のステップを強調する。
4. 操作担当 UID でサインインし、現在の状態で有効な進行ボタンを押す。サイトが `/control/request` に命令を送り、Unity が実行して `/control/ack` に結果を書いたら、画面に実行結果が表示される。閲覧専用 UID では操作ボタンは無効になる。
5. Unity を止めて 15 秒ほどで「更新停止」、30 秒ほどで「オフライン」になること、再起動後に再び値が変わることを確認する。古い命令や前回起動時の命令は再実行しない。

### 遠隔操作の判定

命令には送信先の Unity セッション ID、確認したゲーム状態と更新番号、Firebase サーバー時刻を付ける。Rules は最新の `/live` が 15 秒以内であり、操作担当 UID と状態が合うときだけ命令を受け付ける。Unity もセッション ID、状態、更新番号、命令の経過時間を確認してからローカル操作と同じ関数を呼ぶ。1 件の命令 ID は同じ Unity 起動中に再実行しない。操作の反映はネットワーク状況により数秒かかる。

## 接続できない場合

| 表示・症状 | 確認すること |
| --- | --- |
| 「設定待ち」 | `firebase-config.json` の 5 項目が埋まっており、GitHub Pages の最新配信が成功しているか。 |
| Unity に「writer settings not found」 | 警告に出た絶対パスに `race-dashboard-writer.json` を置いたか。 |
| Unity に sign-in 失敗 | Authentication のメール／パスワードが有効か、書込アカウントの値が正しいか。 |
| Unity に upload 401 | 書込ユーザーが無効化されていないか。再ログイン後も続くなら設定を確認する。 |
| Unity に upload 403 / 400 | Rules の `writerUid`、`databaseURL`、送信データのスキーマを確認する。 |
| 公開画面に「閲覧権限がありません」 | 閲覧ユーザー UID が `roles/viewers` に `true` で登録されているか。 |
| ボタンが無効 | UID が `roles/operators` と `roles/viewers` の両方に登録され、Unity の更新が 15 秒以内か、今の状態で許可される操作か。 |
| 命令を送信できない | Rules を最新版に更新したか、`/live` が新しいか、別の命令が応答待ちでないか。 |
| Unity が操作を受け付けない | Unity のゲーム状態が押した直後に変わっていないか、Unity PC の時刻が正しいか。 |
| 公開画面が更新停止 | Unity Play/ビルドが実行中か、`/live/updatedAt` が更新されているか、ネット接続を確認する。 |

Firebase Spark の現在の無料枠は [公式料金表](https://firebase.google.com/pricing)にある。映像は転送量を抑えるため遠隔配信しない。イベント中に Firebase Console でダウンロード量を測り、無料枠へ近づく場合は送信間隔を長くする。
