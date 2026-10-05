# プレイヤー別BGM・USB/HDMI音声出力

`bgm` ブランチでは、P1/P2それぞれに独立したステレオ音声を、別々のUSB/HDMI音声機器へ送ります。OSで認識される出力機器を2台接続してください。ディスプレイの割り当てと音声機器の割り当ては別です。

## 曲と視点

- 通常走行・カウントダウン: `Assets/Resources/Audio/BGM/racegame_v3.mp3`
- 最終周: `Assets/Resources/Audio/BGM/racegame_v3_final lap.mp3`
- タイトル・リザルト: 既存の `TachibanaMoroe.mp3`

完了周回数が `Goal Lap - 1` になると、そのプレイヤーのBGMだけをFinal Lap曲の先頭から再生します。1周レースではスタートからFinal Lap曲です。ゴール演出中は本人の曲を継続し、観戦開始後は観戦対象の周回に合わせます。エンジン音は各画面の実際のカメラ座標から車両までの距離と左右方向で音量・ステレオ定位を計算します。近くの相手車も聞こえ、80m以上離れると聞こえなくなります。

## 出力先の設定

1. OSの音声設定でUSB/HDMI出力機器が認識されていることを確認してゲームを起動します。
2. 初回は列挙された1台目をP1へ割り当てます。P2は設定から出力機器を選択してください。
3. レース中にESCでメニューを開き、`設定` の `P1 出力` / `P2 出力` ボタンをクリックして機器を選びます。両画面のメニューから設定できます。
4. 同じ機器を両プレイヤーへ同時に割り当てることはできません。割り当てを交換する場合は、片方を一旦 `出力なし` にしてからもう片方を変更します。
5. 変更はOSの機器IDで保存され、次回起動時に復元します。USBの抜き差し後は `再検出` を押してください。保存された機器がない場合、そのプレイヤーの音声を停止して未接続と表示します。

Master/BGM/エンジン音のスライダーは既存どおり両プレイヤー共通です。ゲーム音声をUnityの既定出力から重ねて再生しないため、既定出力がP1機器であってもP2の曲は混ざりません。

## ネイティブプラグイン

macOS (Intel/Apple Silicon) とWindows x64用のプラグインを `Assets/Plugins/RaceAudio` に含めます。macOSはCore Audio、WindowsはWASAPIを使用します。音声のデコードはUnity、機器の列挙・独立出力はminiaudio 0.11.23です。固定版のソースとライセンスは `Native~/RaceAudio` にあります。Unityの音声スレッドから各機器のリングバッファへPCMを送信し、機器側でBGMと2台のエンジン音を混合します。機器のネイティブサンプルレートへの変換はminiaudioが行います。

再ビルド:

- macOS: `sh Native~/RaceAudio/build-macos.sh`
- Windows: Visual Studioの「x64 Native Tools Command Prompt」で `Native~\RaceAudio\build-windows.cmd` を実行します（C++デスクトップ開発ワークロードが必要）。
- オフライン音声ミキサーテスト: `clang++ -std=c++17 -O2 Native~/RaceAudio/test-mixer.cpp -framework CoreFoundation -framework CoreAudio -framework AudioToolbox -lpthread -o /tmp/race-audio-test && /tmp/race-audio-test`
- Unity動作検証: `Racing > Validate Player Audio` または `-batchmode -executeMethod PlayerAudioValidation.Run -quit`。

実機ではP1だけ最終周に進めたときにP2の曲が変わらないこと、P2の最終周、観戦への切り替え、リトライで通常曲へ戻ること、機器の切断・再検出、音量変更と両機器の左右定位を確認してください。
