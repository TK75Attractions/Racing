# USBで管理画面を使う

無線が混雑しているときは、USB経由で同じ管理画面を使えます。通常のDashboard中継は起動したまま使います。RACEやUnityを再起動する必要はありません。

## iPhone: インターネット共有なしで使う

標準は、Safariで使う[有線LAN接続](DashboardWired.md)です。対応LANアダプターとLANケーブルでiPhoneとMacをつなぎます。iPhoneアプリ・Xcode・開発者モード・インターネット共有は不要です。通常の `Start USB Dashboard.command` または `Start Wired Dashboard.command` で準備画面を開きます。

通常のUSBケーブル1本で使う[専用iPhoneアプリ方式](DashboardIPhone.md)は、`--iphone` を明示した場合にだけ起動します。アプリの署名・インストールとiPhoneの許可が必要です。

従来のUSBテザリングを使う場合だけ、コマンドへ `--tethering` を付けます。この追加方式にはインターネット共有対応端末が必要です。専用アプリによる接続ではインターネット共有をオンにしません。

## Android

1. PCに公式の [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools) を用意します。
2. 端末の開発者向けオプションからUSBデバッグを有効にし、USB接続後にこのPCの許可を端末で確認します。
3. Macで `Tools/DashboardUsb/Start Android USB Dashboard.command` を開きます。
4. 接続したAndroidのChromeで `http://127.0.0.1:8766/` を開きます。この経路はUSBポート転送で、画面上の表示は「ローカル / WebSocket」です。

物理USBの端末1台だけを選び、Androidの8766番をPCの管理中継8766番へ転送します。Wi-Fi ADBを選ばず、既存の別の転送を上書き・削除しません。USBを抜いた場合は、再接続後に同じコマンドを開き直します。USBデバッグ・信頼設定を補助アプリが勝手に変更することはありません。[Googleのポート転送仕様](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server)に基づきます。

## 接続できない場合

- USB待機のまま: ケーブル、端末の信頼、専用アプリ、接続コードを確認します。給電専用ケーブルでは通信できません。
- USBページは開くがUnity未接続: 通常のDashboard中継とゲームが起動しているか確認します。ページはゲーム停止中にも開けますが、ゲームの操作・映像は起動後です。
- Androidで転送できない: `adb`、USBデバッグ、このPCの許可、同時に接続している端末の数を確認します。別の8766番転送がある場合は、その所有元を確認します。
- Safariから使うUSBテザリングと、専用アプリのUSB通信は別の方式です。専用アプリの接続ではインターネット共有は不要です。

## 実装と検証範囲

追加のテザリング方式を有効にした場合だけ、専用のUSBネットワークのIPアドレスで待ち受けます。通常のLAN・Wi-Fiや、USBの映像アダプターには公開しません。USBの相手機器、Host、Origin、接続用URLを確認し、Cookieで接続を維持します。抜き差しで接続用URLを更新し、操作のID・対象セッション・状態番号はそのまま通常の管理中継へ渡します。

プライベートHTTPアドレスでも操作IDを生成できるよう、HTTPS専用の `crypto.randomUUID` がない環境では `crypto.getRandomValues` によるUUID v4を使います。映像と状態・操作APIは同じUSB用URLにまとめます。通知の登録など、HTTPSを要するブラウザー機能は従来のTailscale経由で利用します。

`sh Tools/DashboardUsb/test.sh` は、実RACEの8765/8766番を使わず、一時ポートと偽の中継で実際のHTTP/WebSocketを検証します。USB端末の実測ではないため、実際のUSB往復遅延や端末固有の通信制限まで確認したことにはなりません。
