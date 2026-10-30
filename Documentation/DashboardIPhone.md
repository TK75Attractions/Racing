# iPhoneからUSBだけで管理する

SafariとLANアダプターを使う場合は[アプリ不要の有線接続](DashboardWired.md)を使ってください。この文書は通常のUSBケーブルと専用アプリを使う追加方式の説明です。

**インターネット共有・Wi-Fi・Tailscaleは不要です。** iPhoneの専用アプリ「Racing USB」とMacの補助アプリを、USBケーブルで直接つなぎます。管理画面の内容・操作・状態・映像はUSBの通信路を通ります。RACEと通常のDashboard中継はそのまま使います。

## 初回だけ必要な設定

1. Macで `Tools/DashboardIPhone/Open iPhone Project.command` を開きます。
2. Xcodeの **Settings → Accounts** で、自分のApple Accountを追加します。
3. RacingUsbターゲットの **Signing & Capabilities → Team** で自分のチームを選びます。Automatically manage signingはオンにします。Bundle Identifierの使用制限が出た場合は、自分のチームで使える識別名に変更します。
4. iPhoneをUSBで接続し、表示されたらこのMacを「信頼」します。
5. 接続したiPhoneをXcodeの実行先に選びます。iPhoneの **設定 → プライバシーとセキュリティ → デベロッパモード** を、端末に表示される説明を確認して有効にします。この操作は**iPhoneの再起動**を伴います。MacのRACEは再起動しません。
6. XcodeでRunを押し、Racing USBアプリをiPhoneへインストールします。署名元の確認を求められた場合は、iPhone側で自分の署名を確認します。

アカウントへのサインイン、証明書・プロファイルの作成、開発者モードの許可は、利用者自身が確認して行います。自動で資格情報を作成・変更するスクリプトは用意していません。Xcodeから追加のiOSコンポーネントを求められた場合は、XcodeのSettings → Componentsで必要なものを確認します。

無料のPersonal Teamも利用できますが、App IDやプロビジョニングには7日間の期限があります。継続運用前に期限と再署名の手順を確認してください。[Appleのアカウント制限](https://developer.apple.com/help/account/basics/about-your-developer-account/)、[署名の手順](https://help.apple.com/xcode/mac/current/en.lproj/dev60b6fbbc7.html)、[デベロッパモードの説明](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device)を参照してください。

## 毎回の接続

1. Macで通常のDashboard中継とRACEを起動しておきます。
2. `Tools/DashboardUsb/Start iPhone USB Dashboard.command` を開きます。従来どおり `sh "Tools/DashboardUsb/Start USB Dashboard.command" --iphone` でも起動できます。Macの準備画面は `http://127.0.0.1:8767/` です。他の接続方式の補助アプリが起動中なら、その補助アプリのターミナルで **Ctrl+C** を押し、起動し直します。RACEと通常のDashboard中継は終了しません。
3. iPhoneをデータ通信対応のUSBケーブルでMacにつなぎ、**Racing USBアプリ**を開きます。Safariは使いません。
4. Macの準備画面に表示された8けたのコードをiPhoneへ入力し、「USBで接続」を押します。
5. 既存の管理画面が表示され、通信方式が **USB直通 / WebSocket** になったら利用できます。

Mac側の補助アプリを再起動するとコードが変わります。iPhone側で新しいコードを入力し直してください。ケーブルをつなぎ直した場合は、アプリを開いたままならUSBで再接続します。アプリをバックグラウンドへ移した場合は接続を停止するため、戻ったら再び「USBで接続」を押します。

ケーブルが抜けると管理画面の上に接続待ちを表示し、操作を止めます。Wi-Fiへ自動で切り替えず、未確認の操作も自動で再送しません。RACEの進行自体は継続します。

## 問題がある場合

- iPhoneが見つからない: データ対応ケーブル、端末のロック解除、このMacの信頼を確認します。
- コード入力画面のまま: Macの補助アプリが起動しているか、新しい8けたコードか、アプリが前面にあるか確認します。
- Macの準備画面に8けたコードが出ず「Safariから有線で操作」と表示される: 有線LAN用の補助アプリが起動しています。その補助アプリだけをCtrl+Cで終了して、`Start iPhone USB Dashboard.command` を開き直し、ブラウザーを再読み込みします。コードはMacのブラウザーに表示され、ターミナルには表示しません。
- USB接続後に管理画面を開けない: 通常のDashboard中継が8766番で起動しているか確認します。RACEの再起動は不要です。
- アプリをインストールできない: XcodeのTeam、署名・プロファイル、必要なiOSコンポーネント、iPhoneのデベロッパモードを確認します。

## 実装と検証

MacはAppleのusbmuxdへ接続し、接続方式がUSBの端末だけを選びます。Wi-Fiでペアリングした端末は対象にしません。iPhoneアプリのループバック専用ポート30220へUSB経由でつなぎ、8けたコードを確認した後、同じUSB接続内で複数のHTTPとWebSocket通信を中継します。Mac側の転送先は通常の管理中継 `127.0.0.1:8766` に固定です。

iPhoneのWebViewもループバックだけに接続します。非公開Cookie、Host、Origin、要求サイズを検査し、HTTPの宛先とOriginをMac側の管理中継に合わせます。操作ID・対象セッション・状態番号・本文は書き換えません。映像と状態配信が動いていても操作を並行して送れます。

- `sh Tools/DashboardIPhone/build-unsigned.sh`: iPhone実機向けarm64コードを署名・インストールなしでビルドします。この出力だけでは実機にインストールできません。
- `sh Tools/DashboardIPhone/test.sh`: 本番と同じSwift通信部品とC#中継を、偽USBデーモン、一時ポート、偽管理サーバーで結合検証します。実RACEの8765/8766番へ操作は送信しません。

実機インストールとiPhone画面上での動作・遅延確認は、署名と端末設定の完了後に行います。偽USBでの成功は実際のUSB遅延やiPhone UIの確認を意味しません。
