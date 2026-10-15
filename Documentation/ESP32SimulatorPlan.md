# ESP32 Serial Lab — RGP/1改訂の設計

現行仕様は[ESP32Simulator.md](ESP32Simulator.md)と[ESP32SerialProtocol.md](ESP32SerialProtocol.md)です。旧4列CSV案をRGP/1の2プレイヤーTEL改訂へ置き換えました。

- SwiftUI／CLIは、P1/P2の実ハンドル角度・独立アクセル・独立ブレーキを操作します。ゲーム側の除算値をwire角度に掛けません。
- `RgpDevice`はCRC・フレーム・SEQ/TIME・状態・CMD/ACK・セッションと重複記録を担当し、UIやPTYから独立させます。
- `SimulatorEngine`は1つのキューでPTY、CMD受信、制御返信、TEL周期、シナリオ、分割送信、送信待ち上限を管理します。TEL停止中もCMD応答を維持します。
- 正常TELは6項目が有効なときだけ一括送信します。センサー無効はERR／EVTとTEL全体の停止に変換し、部分更新を行いません。
- `MacPseudoTerminalLineReader`は明示的なmacOS PTYに対するraw byteのread/writeだけを担当し、物理USBと同じ本番RGP受信処理を利用します。
- Editor接続情報はversion=2、wireProtocol=RGP/1。Editorセッションだけに適用し、保存済みシーンとPlayer Settingsには書き込みません。
- 共通vectors・Swift単体検査・実PTYのSwift→本番C#双方向検査・UnityコンパイルとEditMode検査を組み合わせます。ゲームビルドの検査は更新済みDevelopment Buildで別途行います。

実機固有のUSB列挙、電気信号、DTR/RTS、センサー精度・電圧校正はシミュレーターの検証範囲外です。
