# iPhone / iPadからUSBだけで管理する

通常のUSBケーブルと専用アプリを使います。無線経由の管理には[Tailscale](DashboardStartup.md)を使用します。

**インターネット共有・Wi-Fi・Tailscale・接続コードは不要です。** 専用アプリ「Racing USB」とMacを、USBケーブルで直接つなぎます。iOS / iPadOS 16以降が対象で、iPadはWi-Fiモデルでも使えます。LightningとUSB-Cどちらもデータ通信対応ケーブルで接続できます。iPad実機での確認は未実施です。

通信経路は `端末のWebView → 端末内のHTTP/WebSocket中継 → USBケーブル → MacのUSB補助アプリ → 127.0.0.1:8766の管理中継 → 127.0.0.1:8765のゲーム` です。USB区間はmacOSのusbmuxdを使い、物理USBの端末だけを選びます。Wi-Fiでペアリングした端末にはつなぎません。USB側では一度に1台のアプリへ接続します。

## 初回だけ必要な設定

1. Macで `Tools/DashboardIPhone/Open iPhone Project.command` を開きます。
2. Xcodeの **Settings → Accounts** で、自分のApple Accountを追加します。
3. 左端の青いRacingUsbプロジェクト → 中央の **TARGETS → RacingUsb → Signing & Capabilities → Team** で自分のチームを選びます。Automatically manage signingはオンにします。Bundle Identifierの使用制限が出た場合は、自分のチームで使える識別名に変更します。
4. iPhoneをUSBで接続し、表示されたらこのMacを「信頼」します。
5. 接続したiPhoneをXcodeの実行先に選びます。iPhoneの **設定 → プライバシーとセキュリティ → デベロッパモード** を、端末に表示される説明を確認して有効にします。この操作は**iPhoneの再起動**を伴います。MacのRACEは再起動しません。
6. XcodeでRunを押し、Racing USBアプリをiPhoneへインストールします。署名元の確認を求められた場合は、iPhone側で自分の署名を確認します。

アカウントへのサインイン、証明書・プロファイルの作成、開発者モードの許可は、利用者自身が確認して行います。自動で資格情報を作成・変更するスクリプトは用意していません。Xcodeから追加のiOSコンポーネントを求められた場合は、XcodeのSettings → Componentsで必要なものを確認します。

無料のPersonal Teamも利用できますが、App IDやプロビジョニングには7日間の期限があります。継続運用前に期限と再署名の手順を確認してください。[Appleのアカウント制限](https://developer.apple.com/help/account/basics/about-your-developer-account/)、[署名の手順](https://help.apple.com/xcode/mac/current/en.lproj/dev60b6fbbc7.html)、[デベロッパモードの説明](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device)を参照してください。

## 毎回の接続

1. 自動起動対応版のRACEをMacで起動します。ローカル管理中継とUSB補助アプリがバックグラウンドで起動します。既に動いている対応版は再利用します。
2. 端末の**最新版Racing USBアプリ**を開き、データ通信対応ケーブルでMacにつなぎます。Safariは使いません。
3. 自動で管理画面が表示されます。接続コードや接続ボタンの操作は不要です。画面に **USB直通 / WebSocket** と表示されたら利用できます。

ケーブルをつなぎ直すと自動で再接続します。バックグラウンドでは接続を止め、アプリを前面に戻すと自動再開します。OSの信頼・ロック解除・アクセサリ許可は引き続きOSの確認に従います。このMacへ物理USBでつなぎ、対応アプリを開いた端末はコードなしで操作できます。

端末のアプリを閉じた状態から、通常のUSBケーブルだけで強制的に起動する機能はありません。必要なら端末の「ショートカット → オートメーション → 充電器に接続 → アプリを開く → Racing USB」を利用できます。これはそのMacとのデータ接続ではなく**給電開始**を検出するため、ほかの充電器でも作動します。ロック中のアプリ起動はロック解除が必要になる場合があります。[Appleのオートメーショントリガー](https://support.apple.com/en-euro/guide/shortcuts/apde31e9638b/9.0/ios/26)

手動起動は `Tools/DashboardUsb/Start iPhone USB Dashboard.command`、または `sh "Tools/DashboardUsb/Start USB Dashboard.command" --iphone` です。準備画面は `http://127.0.0.1:8767/`。他方式や旧PIN版の補助アプリが起動中なら、その補助アプリだけをCtrl+Cで終了して起動し直します。RACEと通常のDashboard中継は終了しません。

## ゲームの自動起動対応

自動起動処理は新しいゲームビルドから有効です。既存の古いRace.appへソース変更だけで追加されるものではありません。Unity PlayでもUSB補助アプリを起動します。既存のEditor側の管理中継起動と重複しないようにします。

Mac版ビルド前に.NET 10 SDKで管理中継とUSB補助アプリを発行し、StreamingAssetsのDashboardHelpers.zipへ同梱します。ゲーム起動時は.NET 10 / ASP.NET Core Runtimeで動作し、SDKやソースフォルダーは不要です（.NET 10 SDK導入済みのMacには両方含まれます）。既存の管理中継・Tailscale設定は変更しません。

補助アプリの準備はゲームのスレッド外で行います。ログはゲームのpersistentDataPath配下 `DashboardHelpers/startup.log` です。検証などで不要なら、ゲーム起動前の環境変数 `RACING_USB_AUTOSTART=0` で無効にできます。ゲーム終了時に補助アプリを停止する処理はなく、稼働中なら次の起動で再利用します。

ケーブルが抜けると管理画面の上に接続待ちを表示し、操作を止めます。Wi-Fiへ自動で切り替えず、未確認の操作も自動で再送しません。RACEの進行自体は継続します。

## 問題がある場合

- iPhoneが見つからない: データ対応ケーブル、端末のロック解除、このMacの信頼を確認します。
- コード欄がある: 端末アプリまたはMac補助アプリが旧版です。端末側はXcodeでRunし直し、Mac側は補助アプリだけを終了して最新版を起動してください。
- USB接続後に管理画面を開けない: 通常のDashboard中継が8766番で起動しているか確認します。RACEの再起動は不要です。
- アプリをインストールできない: XcodeのTeam、署名・プロファイル、必要なiOSコンポーネント、iPhoneのデベロッパモードを確認します。

## 実装と検証

MacはAppleのusbmuxdへ接続し、接続方式がUSBの端末だけを選びます。端末アプリのループバック専用ポート30220へUSB経由でつなぎ、v2の自動接続手順を確認した後、同じUSB接続内で複数のHTTPとWebSocket通信を中継します。Mac側の転送先は通常の管理中継 `127.0.0.1:8766` に固定です。旧PIN方式のv1とは互換性がありません。

iPhoneのWebViewもループバックだけに接続します。非公開Cookie、Host、Origin、要求サイズを検査し、HTTPの宛先とOriginをMac側の管理中継に合わせます。操作ID・対象セッション・状態番号・本文は書き換えません。映像と状態配信が動いていても操作を並行して送れます。

- `sh Tools/DashboardIPhone/build-unsigned.sh`: iPhone実機向けarm64コードを署名・インストールなしでビルドします。この出力だけでは実機にインストールできません。
- `sh Tools/DashboardIPhone/test.sh`: 本番と同じSwift通信部品とC#中継を、偽USBデーモン、一時ポート、偽管理サーバーで結合検証します。実RACEの8765/8766番へ操作は送信しません。
- `sh Tools/DashboardIPhone/test-startup.sh`: 起動処理とビルドフックをスタブ付きでコンパイルし、既存中継の再利用、ソースからの起動、同梱版の展開と起動計画を検証します。ゲームや実プロセスは起動しません。

実機インストールとiPhone画面上での動作・遅延確認は、署名と端末設定の完了後に行います。偽USBでの成功は実際のUSB遅延やiPhone UIの確認を意味しません。
