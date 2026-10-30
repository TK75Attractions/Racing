# Safariから有線LANで管理する

iPhoneへアプリをインストールせず、Safariで既存の管理画面を使います。インターネット接続・インターネット共有・Xcode・デベロッパモードは不要です。Mac側では通常のDashboard中継と有線補助アプリを使います。

接続は `iPhone → LANアダプター → LANケーブル → MacのEthernetポート` です。普通のUSB充電・通信ケーブル1本でMacとiPhoneをつないでも、この方式にはなりません。Safariは[WebUSBを実装していません](https://github.com/WebKit/standards-positions/issues/68)。

## 機材

- Lightning端子のiPhone（今回確認した端末はiPhone 12）: 対応Lightning–Ethernetアダプター、またはLightning–USB 3カメラアダプターと対応USB-A Ethernetアダプター。必要に応じてカメラアダプターへ給電します。[Appleの対応機材・給電説明](https://support.apple.com/en-ie/111811)を確認してください。
- USB-C端子のiPhone: その端末とiOSに対応するUSB-C–Ethernetアダプター。[AppleのUSB-C接続説明](https://support.apple.com/105099)を参照してください。
- Mac側の専用Ethernetポート（なければ対応USB–Ethernetアダプター）とLANケーブル。

Macの普段のネットワークとは別の有線ポートを使い、iPhoneと直接つなぎます。既存LANへの接続やMacのインターネット共有は必要ありません。

## 初回の設定例

既存のネットワークと重ならない、専用のプライベートサブネットを選びます。以下は例です。

| 設定 | Macの専用Ethernet | iPhoneのEthernet |
| --- | --- | --- |
| IPv4の構成 | 手動 | 手動 |
| IPアドレス | 192.168.77.1 | 192.168.77.2 |
| サブネットマスク | 255.255.255.0 | 255.255.255.0 |
| ルーター・DNS | 空欄 | 空欄 |

1. LANアダプターとLANケーブルを接続します。Lightningアダプターに電力が必要なら給電します。
2. Macのシステム設定 → ネットワークで、専用Ethernetの詳細 → TCP/IPからIPv4を手動設定します。既存のWi-Fiやゲーム用ネットワークは変更しません。
3. iPhoneの設定に出るEthernetで、接続したアダプターのIPv4を手動設定します。Ethernet項目が出ない場合は対応機材・給電・ケーブルを確認します。
4. Macで `Tools/DashboardUsb/Start Wired Dashboard.command` を開きます。準備画面は `http://127.0.0.1:8767/` です。既存の `Start USB Dashboard.command` も標準ではこの有線方式を開きます。
5. 準備画面で専用の有線ポート / MacのIPを選び、iPhoneに設定したIPを入力して「この有線ポートで開始」を押します。このボタンが始めるのは管理用の待受です。レース開始操作ではありません。
6. 表示された接続用URLをiPhoneのSafariで開きます。管理画面の通信表示が「有線LAN / WebSocket」になることを確認します。

補助アプリはOSのIP設定・ルーティング・共有設定を変更しません。通常の管理中継やRACEを起動・停止・更新する処理もありません。

## 運用

接続用URLはペアリング情報を含みます。運用者だけで扱ってください。選んだMacの有線IPだけで待ち受け、指定したiPhoneのIPと接続用URLを持つ端末だけを許可します。Wi-Fi・ブリッジ・映像アダプターを有線候補へ自動採用しません。

ケーブル切断やMac側IP変更を検知すると、待受を止めます。準備画面で開始し直し、新しいURLをSafariで開きます。Wi-Fiへ切り替えて操作したり、未確認の操作を自動再送したりはしません。端末のIPが同じサブネット内の別アドレスになった場合も設定し直してください。

Safariの共有メニューからホーム画面へ追加することもできます。ただし待受を開始し直すとペアリングが変わるため、まず新しい接続用URLで開いてください。HTTPのローカル接続なのでプッシュ通知などHTTPS必須の機能は使いません。

## 検証範囲

`sh Tools/DashboardUsb/test.sh` は一時ポートと偽の管理中継だけを使います。有線インターフェースの選択、同一サブネット内の別のプライベートIPへの制限、Host/Origin/接続用URL、HTTP操作本文の維持、画像、WebSocket、切断を検証します。実RACEの8765/8766番に操作を送りません。

実際のLANアダプターを使うiPhoneの画面・接続安定性・往復遅延の確認は、機材接続後に行います。テスト成功だけでは実機確認済みにはなりません。
