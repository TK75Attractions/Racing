# USBで管理画面を使う

無線が混雑しているときは、USB経由で同じ管理画面を使えます。通常のDashboard中継は起動したまま使います。RACEやUnityを再起動する必要はありません。

## iPhone / USBインターネット共有に対応するiPad

1. 端末とゲーム用Macをデータ通信対応のUSBケーブルで接続し、端末に表示された「信頼」を許可します。
2. 端末の「インターネット共有」をオンにします。MacのWi-Fiは切らず、そのままで構いません。
3. Macで `Tools/DashboardUsb/Start USB Dashboard.command` を開きます。
4. 開いた画面で「USB接続を確認しました」と表示されたら、USB用URLをコピーし、接続した端末のSafariで開きます。
5. 管理画面の通信方式が **USB直通 / WebSocket** なら、USBの経路で接続しています。

初回だけ.NET 10 SDKが必要です。USB補助アプリのターミナルは開いたままにします。同じ補助アプリを繰り返し開く必要はありません。

これはUSBテザリングのネットワークを使います。通常のケーブルを挿しただけでは、Safariの通信はUSBに変わりません。インターネット共有に対応する回線と端末が必要です。**Wi-FiモデルのiPadでは、通常のUSBケーブルだけでこの方式は利用できません。** [AppleのUSB接続手順](https://support.apple.com/ja-jp/guide/mac-help/mchl7403f0ee/mac)も参照してください。

USBの抜き差しで接続先が変わった場合は、新しく表示されたUSB用URLを使います。無線で使い直す場合は、これまでのTailscale用URLを開きます。通信経路は自動で混在させません。

## Android

1. PCに公式の [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools) を用意します。
2. 端末の開発者向けオプションからUSBデバッグを有効にし、USB接続後にこのPCの許可を端末で確認します。
3. Macで `Tools/DashboardUsb/Start Android USB Dashboard.command` を開きます。
4. 接続したAndroidのChromeで `http://127.0.0.1:8766/` を開きます。この経路はUSBポート転送で、画面上の表示は「ローカル / WebSocket」です。

物理USBの端末1台だけを選び、Androidの8766番をPCの管理中継8766番へ転送します。Wi-Fi ADBを選ばず、既存の別の転送を上書き・削除しません。USBを抜いた場合は、再接続後に同じコマンドを開き直します。USBデバッグ・信頼設定を補助アプリが勝手に変更することはありません。[Googleのポート転送仕様](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server)に基づきます。

## 接続できない場合

- USB待機のまま: ケーブル、端末の信頼、インターネット共有と端末の対応を確認します。給電専用ケーブルでは通信できません。
- USBページは開くがUnity未接続: 通常のDashboard中継とゲームが起動しているか確認します。ページはゲーム停止中にも開けますが、ゲームの操作・映像は起動後です。
- Androidで転送できない: `adb`、USBデバッグ、このPCの許可、同時に接続している端末の数を確認します。別の8766番転送がある場合は、その所有元を確認します。
- iPadがWi-Fiモデル: このUSBテザリング方式は使えません。USB Ethernetアダプターによる有線LANなど、端末に対応する別の経路が必要です。

## 実装と検証範囲

Apple端末の補助アプリは、専用のUSBネットワークのIPアドレスだけで待ち受けます。通常のLAN・Wi-Fiや、USBの映像アダプターには公開しません。USBの相手機器、Host、Origin、接続用URLを確認し、Cookieで接続を維持します。抜き差しで接続用URLを更新し、操作のID・対象セッション・状態番号はそのまま通常の管理中継へ渡します。

プライベートHTTPアドレスでも操作IDを生成できるよう、HTTPS専用の `crypto.randomUUID` がない環境では `crypto.getRandomValues` によるUUID v4を使います。映像と状態・操作APIは同じUSB用URLにまとめます。通知の登録など、HTTPSを要するブラウザー機能は従来のTailscale経由で利用します。

`sh Tools/DashboardUsb/test.sh` は、実RACEの8765/8766番を使わず、一時ポートと偽の中継で実際のHTTP/WebSocketを検証します。USB端末の実測ではないため、実際のUSB往復遅延や端末固有の通信制限まで確認したことにはなりません。
