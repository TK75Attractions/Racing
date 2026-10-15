# ESP32 Serial Lab

Mac用のESP32シリアル入力シミュレーターです。

リポジトリのルートで実行します。

```sh
bash Tools/ESP32Simulator/build.sh
bash Tools/ESP32Simulator/test.sh
```

出力: `Builds/ESP32Simulator/ESP32 Simulator.app`と`esp32-simulator-cli`。

アプリでポートを作成し、Unityの`Racing > ESP32 Simulator`から接続します。
操作・JSON仕様・CLI・Macゲームビルドの手順は[利用ガイド](../../Documentation/ESP32Simulator.md)を参照してください。

Unity付属Monoの`SerialPort`はPTYのDTR/RTS操作に失敗するため、Unity側にはシミュレーター専用のPOSIX読取アダプターを同梱しています。CSV解析と入力の有効性・タイムアウト判定は既存処理を使用します。
