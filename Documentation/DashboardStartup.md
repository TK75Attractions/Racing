# ダッシュボード 起動マニュアル

## 1. PCで起動する

1. ゲーム用Macを起動し、ログインします。
2. Tailscaleを開き、接続をオンにします。
3. Unityで「Racing」プロジェクトを開きます。
4. 上部メニューの **Racing > Start Dashboard and Play** を選びます。
5. ブラウザーで管理画面が開き、**「UNITY 接続中」** と表示されたら起動完了です。

PCで直接開く場合：<http://127.0.0.1:8766/>

## 2. スマホで開く

1. スマホのTailscaleを開き、PCと同じアカウントで接続をオンにします。
2. SafariまたはChromeの新しいタブで、接続先URLを開きます。
3. **「UNITY 接続中」** と表示されたら利用できます。

接続先URLは、PCのUnityで **Racing > Dashboard > 別端末用URLをコピー** から取得します。

PCとスマホは別のWi-Fiや、スマホの4G・5Gでも接続できます。

## スマホで初めて使うとき

App StoreまたはGoogle Playで「Tailscale」をインストールします。PCと同じアカウントでログインし、VPN構成・接続の許可を承認してから、上の手順で接続します。

## 中継を手動で起動する場合

FinderでRacingフォルダー内の **Tools > DashboardRelay > Start Dashboard.command** をダブルクリックします。表示されたターミナルは開いたまま使います。

このMacは中継の自動起動を設定済みです。Unity停止中もページは開けます。レース情報を表示するにはUnityをPlayしてください。利用中はMacを起動・ログインしたまま、スリープさせずに使います。
