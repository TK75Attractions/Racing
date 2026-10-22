# 外付け2画面への自動起動配置（2026-10-22）

機能commit: `245ddff`、起動フォーカス補強: `b333c0a`、統合ビルド: `26179ff`。Unity 6000.3.9f1。

macOS / Metal向け。外付け画面をCGDisplayIsBuiltinで判別し、独立した2台があればP1/P2をそれぞれの全領域へ配置する。Unityの画面番号・macOSメイン画面の選択には依存しない。フルスクリーンSpaceを作らず、P1のキーボードフォーカスとP2の独立描画を維持する。仕様・起動条件は [TwoPlayerLocalSetup](../../Documentation/TwoPlayerLocalSetup.md)。

## 検証

[native-tests.txt](native-tests.txt): 画面構成12ケースと、本番AppKitヘルパーを使った非表示ウィンドウ試験。

- 内蔵＋外付け0／1／2台、外付けがOSメイン画面、内蔵画面なし、3台以上。
- 画面の列挙順が変わっても、物理配置に基づくP1/P2の選択が同じ。
- 縦置き、上下配置、負座標、ミラーリング、同じ物理IDの重複、不正サイズ。
- 実際のNSWindowで1920×1080／1440×2560／2880×1800／3840×2160のコンテンツ領域、画面全体の座標、タイトル非表示を検査。
- P1のキーウィンドウ適格性とP2のフォーカス非取得、main/keyでない起動前の非表示ウィンドウの検出、同じサイズでの再適用なし、元のウィンドウ形式・位置への復帰。

テスト用AppKitウィンドウは非表示のまま実行する。画面構成のケースは入力データを使う選択試験であり、実際に物理モニターを接続した試験ではない。

この環境では外付け2台の接続を確認できないため、実機2台での同時表示・接続し直し・各機種のRetina倍率は未検証。ネイティブテスト、異解像度の描画先/UI回帰試験、Macビルドと利用可能な画面での起動確認を分けて記録する。

## Macビルドと起動確認

`MultiDisplayValidation.BuildMac` によりUniversal Macアプリを作成。最新mainの音響・FPSダッシュボード等の変更を含む統合ビルドでも、[既存画面出力の回帰試験](display-regression.txt) とビルドが通過した。Render Graph、表示先の独立性、異解像度のカメラ／UI、出力ターゲットの再生成・解放・交互バッファへの完全な転送を検査している。

[Standaloneレポート](standalone/report.json)。利用可能な画面はUnityの報告で1つ。P1/P2の独立ウィンドウへフォールバックし、P1の画面を [p1.png](standalone/p1.png)、P2のMetal出力を [p2-presented.png](standalone/p2-presented.png) に保存した。更新されたプラグインのロード、実際のカメラ／HUD描画、終了を確認し、レポートの `errors` は空。入力は実機ESP32の代わりに所有する一時PTYのRGP/1シミュレーターを使用。本番のシーン・機器設定やOSのディスプレイ設定は変更していない。既存の描画計測プローブによる固定場面の起動試験であり、運転操作や物理的な2画面の試験ではない。

最初のシミュレーターなしの起動では、表示処理とは別に「ESP32が未接続」の2件を記録した。上記の正式な起動確認は一時PTYを接続して取り直したもの。

## 再実行

```sh
sh Native~/RaceWindows/test.sh
sh Native~/RaceWindows/build-macos.sh
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" -executeMethod MultiDisplayValidation.BuildMac \
  -quit -logFile /tmp/racing-external-display-build.log
```

生成先: `Builds/DisplayValidation/Race.app`。通常のBuild and Runでも、同梱される更新済みUniversalプラグインと起動処理が同じ自動配置を行う。

## APIの根拠

内蔵画面判別はAppleの [CGDisplayIsBuiltin](https://developer.apple.com/documentation/coregraphics/cgdisplayisbuiltin(_:))、配置は [NSScreen](https://developer.apple.com/documentation/appkit/nsscreen) のframeと [NSWindow.setFrame](https://developer.apple.com/documentation/appkit/nswindow/setframe(_:display:)) に基づく。macOSが拡張画面として公開していない鏡像出力を、独立したゲーム画面として扱うことはしない。
