# DisplayLink表示・独立音声出力の安定化

- 追加画面の即時描画を、SRPのCommandBufferによる全面転送と明示的なSubmitへ変更。色バッファのLoadはDontCare、StoreはStore、深度バッファは不要。完成したカメラスタックを1フレームに1回だけ転送する。
- PCのVSyncとRun In Backgroundを有効化。車の物理演算・入力・カメラ補間は変更していない。
- 音声は50msまたはDSPブロック3個分を蓄えてから再生。機器ごとのクロック差を微小なリサンプルで吸収し、バッファ切れ時はフェードして再充填する。
- Unityの既定機器変更通知でDSPサンプルレートが同じ場合、独立音声出力を再オープンしない。
- macOS用プラグインをarm64/x86_64で再ビルド。Windows DLLは従来版のため、Windowsではソースから再ビルドが必要。

## 検証

`Native~/RaceAudio/test-mixer.cpp` をAddressSanitizer・UndefinedBehaviorSanitizer付きでコンパイルし実行。2台の機器を±1500ppmのクロック差、480/960フレームのコールバック、1024フレームの入力に−4〜+6msの転送揺れとして600秒分を模擬した。

```
600s clock +1500ppm: zero underruns, queue 1167..3271 frames
600s clock -1500ppm: zero underruns, queue 844..3803 frames
PASS: stereo/device/CPU isolation, mono, clipping, priming, starvation/recovery, clock drift, DSP jitter, bounded concurrent queues
```

Unityの `Racing > Validation > Display Outputs` は異なる解像度・リサイズ・UI倍率・カメラスタックの完成判定・同一フレーム内の重複転送抑止に加え、交互の描画先へ8回GPU転送し全画素を読み戻す。実際のDisplayLink接続時のちらつきとUSB/HDMI機器での聴取は、このオフライン検証の対象外。

手順と設計は `Documentation/TwoPlayerLocalSetup.md`、`Documentation/PlayerAudio.md` を参照。

Unity 6000.3.9f1で最終確認:

- `MultiDisplayValidation.Run`: PASS（描画先・UI・GPU転送・重複抑止）
- `PlayerAudioValidation.Run`: PASS（BGM切替・観戦・出力分離・同一DSPレートの変更通知・ネイティブ機器列挙）
- `StandalonePerformanceValidation.Validate`: PASS（既存の15本の木のLOD・AOを維持）
- `StandalonePerformanceValidation.BuildRelease`: PASS（一時的なBurst無効化オプションを外した通常コマンド、2026-10-15 JST）

修正版Macアプリ: `Builds/StandaloneRelease/Race.app`。実行時のDisplayLink表示／実際の音声出力での効果は未検証。
