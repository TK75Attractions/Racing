# Tailscale経由の統合ダッシュボード

既存の画面でレース管理、順位・CPU状態、プレイヤープレビュー、シリアル最新値、通信履歴を使う。ゲームPCと閲覧端末は別回線でよく、両方にTailscaleを導入して接続する。

## 構成

```text
ESP32 / Serial Lab → USB / PTY → Unity
                                  ├─ ゲーム入力
                                  └─ 受信スレッドの有限ジャーナル
                                      ↓ ローカルNDJSONストリーム
                              .NET WebSocket補助ブリッジ
                                      ↓ Tailscale Serve / HTTPS・WSS
                              同じダッシュボードを別端末で表示
```

UnityのMono環境では標準のHttpListener.AcceptWebSocketAsyncが未実装だったため、WebSocketはASP.NET Coreで処理する。追加NuGetパッケージは不要。Unityと補助ブリッジはともに127.0.0.1だけで待ち受け、Serveを接続の入口にする。

- Unity: `http://127.0.0.1:8765/`
- 補助ブリッジ: `http://127.0.0.1:8766/`
- 別端末: Serveが表示する `https://<PC>.<tailnet>.ts.net/`

HTTPの画面・API・JPEGとWebSocketを同じURLにまとめる。HTTPS画面からループバックHTTPへ接続しない。GitHub Pagesの既存画面はFirebase方式のまま利用でき、自動で操作先を切り替えない。

## かんたんな開始方法

初回だけゲームPCに[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)を入れる。別回線の端末から使う場合は、ゲームPCと閲覧端末に[Tailscale](https://tailscale.com/download)を入れ、ログイン・接続する。

### Unity Editorから使う

1. UnityでPlayする。補助ブリッジが自動ビルド・起動する。`Racing → Start Dashboard and Play`なら、必要に応じてSampleSceneを開いてPlayまで一度に開始できる。
2. `Racing → Dashboard`で「ダッシュボードを開く」を押す。
3. 別端末では、表示された「別端末用URL」をコピーして開く。ダッシュボード上部の「URLをコピー」も使える。

ターミナルや環境変数の編集は不要。操作担当者は、既定ではゲームPCにログイン中のTailscaleユーザーを自動取得する。別アカウントも操作担当にする場合は、画面の「操作担当者（任意）」へTailscaleログイン名をカンマ区切りで入力して起動し直す。空欄に戻すと自動取得へ戻る。

Play終了時・スクリプト再読み込み時・Editor終了時には、Editorが起動した補助ブリッジを停止する。既に別の場所で動いているブリッジは再利用し、そのプロセスは停止しない。自動起動は同じ画面のチェックで切り替えられる。設定はEditorPrefsに保存し、シーンは変更しない。

### Finder / エクスプローラーから使う

UnityのPlayまたはゲームの起動後に、次のファイルをダブルクリックする。

- macOS: `Tools/DashboardRelay/Start Dashboard.command`
- Windows: `Tools/DashboardRelay/Start Dashboard.cmd`

補助ブリッジをビルド・起動し、ブラウザーを開く。ターミナルを閉じるとブリッジを停止する。ゲームPC内だけで使う場合はTailscale不要でWebSocketを利用できる。

### 初回のTailscale設定

Tailscaleが利用可能なら、PCのHTTPSアドレスとログイン名を取得し、`tailscale serve --bg --yes --https=443 http://127.0.0.1:8766`で遠隔接続を準備する。既存の接続先が同じならそのまま再利用する。ログイン前や未導入の場合はローカルで起動し、必要な操作を表示する。

初回のHTTPS有効化には本人の承認が必要な場合がある。Unityの「Tailscaleの初回HTTPS設定を開く」、または起動時に表示されたTailscaleのURLから完了し、もう一度起動する。インストール・ログイン・Tailscaleのアクセス許可設定は自動で代行しない。

既存の443番ポートやHTTPSのルートを別サービスが使用している場合、設定を上書きせずローカル起動に切り替える。Funnelの公開設定がある場合も自動設定を止める。Serve設定はPCのTailscaleへ保存されるが、実際の配信にはゲームと補助ブリッジの両方が必要。[Serveの公式仕様](https://tailscale.com/docs/reference/tailscale-cli/serve)

### 従来の設定を指定する場合

`run.sh`は従来どおり`relay.env`を、`run.ps1`は`relay.env.ps1`を読み込める。`RACING_DASHBOARD_ORIGIN`と`RACING_DASHBOARD_OPERATORS`を指定すると、その値を優先する。これらのファイルはGit管理対象外。Unityからの起動では、環境変数と設定画面の操作担当者欄を使う。

閲覧者の接続許可はTailscaleのアクセスルールで制限する。操作担当者は各自のTailscaleアカウントで接続する。タグ付き端末にはログインユーザーの認証情報が付かないため、操作担当者の自動取得は行わない。

Serveは利用者ヘッダーを付加し、外から同名ヘッダーを持ち込まれた場合は除去する。補助ブリッジはHost・Originと利用者情報を検査し、POSTの操作担当者を毎回確認する。8765/8766をLANやインターネットへ直接公開しない。[認証情報の仕様](https://tailscale.com/docs/features/tailscale-serve)

## 表示と配信

- 正常なTELは履歴・自然言語説明の対象外。最新の受信時刻、P1/P2のHANDLE・ACCEL・BRAKEと原文だけを最新値欄へ更新する。ゲームに実際に適用された値はプレイヤーの入力表示で確認する。
- TEL以外のSYS、EVT、ACK、ERR、DBG、IMUと送信CMDを、日本語説明・送受信方向・原文付きの履歴へ表示する。CMDのOPEN_SESSIONに含まれる識別子は原文以外の説明には繰り返さない。
- CRCや形式の不正なTELは解析エラーとして履歴へ残す。入力途絶、I/O異常、非互換バージョン、コマンド応答タイムアウトも表示する。
- 受信・送信時点でジャーナルへ記録するため、Unityのフレーム更新が止まっても通信履歴の配信は継続できる。適用結果を未確認の受信データはRECEIVEDとして扱う。
- Unityはバックグラウンド実行を有効にし、ブラウザーへ切り替えても状態・操作APIを止めない。状態は約10Hz、受信ジャーナルは約20msごとに変更分を配信する。描画は約30Hz、カメラ画像は約2秒ごと。カメラ画像は低遅延動画ではない。
- ジャーナルは最大5000件か4MiBまでの非TEL履歴と、独立した最新TEL・直近8件の警告/エラー。ブラウザー表示は直近500件。接続ごとに最大128件ずつ補完する。保持範囲を超えた再接続では欠落を明示する。USB再接続・シミュレーター切り替えではジャーナルのセッションを更新する。
- 同時WebSocket接続は最大8。遅い閲覧者の送信は3秒で打ち切り、ゲーム側を待たせない。Unity側のストリームも最大8、通常HTTP待ち行列は最大64。
- 4秒間受信がなければ切断・再接続。Unity状態が3秒更新されなければログ配信中でも操作を停止する。RTTはPCへのHTTP往復時間であり、シリアルから画面までの片道遅延ではない。

## 操作の整合性

命令にはID、Unity起動セッション、想定状態、状態変更番号、直近スナップショットのPC時刻を付ける。Unityは15秒以内か、セッションと状態が一致するかを確認してメインスレッドで実行する。同一ID・同一内容は保存した結果を返し、異なる内容でのID再利用は拒否する。結果は直近256件を保持する。応答が失われた場合は同じIDの結果を照会し、操作を自動再送しない。

## 検証

```sh
sh Tools/Validate-Rgp.sh
node --test Tests~/Dashboard/dashboard-ui.test.cjs
sh Tools/DashboardRelay/test.sh
sh Tools/ESP32Simulator/test.sh
```

`Tools/DashboardRelay/test.sh`は実際のUnityブリッジ・ジャーナル・受信コードをコンパイルし、HTTP→WebSocketをループバックで検査する。シーン・カメラ・レース処理はテスト用の代替を使用する。実際のUnityコンパイルと既存の状態生成は`NpcDashboardValidation.Run`で別途確認する。

PlaywrightとChromeを利用できる環境では`RACING_BROWSER_TEST=1 sh Tools/DashboardRelay/test.sh`で実ブラウザーの統合検査とPC/スマホ幅の画像保存も行う。出力先は`Artifacts/DashboardRealtime/`。`RgpSerialJournalTests`の18万TELは100Hz×30分相当の**件数の加速試験**であり、30分の実時間耐久試験ではない。

別回線の実機確認には、Tailscaleへのログイン、ServeのHTTPS有効化、アクセス許可設定、閲覧端末での接続が必要。コードの自動検査だけで、回線遅延や実機カメラ負荷まで確認したことにはしない。

Serial LabをビルドしたmacOSでは`RACING_PTY_TEST=1 sh Tools/DashboardRelay/test.sh`で実PTY→WebSocketの連続入力も検査できる。100Hz設定で15秒間測定し、独立ペダル値・CMD/ACK履歴・TEL非蓄積と同一PC内の受信時刻からの遅延を`pty-websocket.json`へ保存する。

起動自動化の検査には、`quickstart.test.cjs`（テスト用Tailscale CLIで設定・保護・停止を検査）と、Unityの`RaceDashboardLauncherValidation.Run`（実際のビルド・起動・重複抑止・停止）を利用する。本番Tailscaleのログインや公開設定は検査では変更しない。
