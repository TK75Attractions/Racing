# ESP32 Serial Lab (RGP/1)

macOS用のシリアル入力テストアプリです。CRC付きの6項目TEL、独立アクセル／ブレーキ、双方向CMD/ACK・セッション、センサー異常と通信停止を本番のUnity受信処理で検査します。旧CSV形式との互換モードはありません。

```sh
bash Tools/ESP32Simulator/build.sh
bash Tools/ESP32Simulator/test.sh
```

アプリは `Builds/ESP32Simulator/ESP32 Simulator.app`、CLIは同じフォルダーに生成します。Unityで `Racing > ESP32 Simulator` を開き、アプリの接続情報を読み込んで有効化してください。

詳細は [ESP32Simulator.md](../../Documentation/ESP32Simulator.md) を参照してください。PTYはPOSIX read/writeだけを使い、CRC・セッション・stale判定はUSB実機と同じコードです。

## WebSocketダッシュボードとの併用

Serial LabからUnityへ接続する手順とRGP/1の送信形式は共通です。[Tailscaleダッシュボード](../../Documentation/RealtimeDashboard.md)の補助ブリッジを起動すると、同じ画面で入力と通信を確認できます。TELは最新値だけを更新し、OPEN_SESSION等の送信CMD、装置のACK/SYS/EVT/ERR/DBG等を履歴へ残します。`test.sh`の実PTY受信検査も、このジャーナルにTELが蓄積せず、送信コマンドを保持することを確認します。
