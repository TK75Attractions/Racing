# オリジナルSE・試聴と検証

`preview.wav` はゲーム用19音源を順番に並べた24.7秒のステレオ試聴です。各音源間に約0.33秒の無音があります。再生順と用途は `Documentation/SoundEffects.md` に記載しています。実際のゲームでは既存のSE音量（初期値0.8）とMaster設定を通して再生します。

- マスター: `Assets/Resources/Audio/SFX/*.wav`（48kHz / 16bit / stereo）。
- 制作ツール: `Tools/BuildRaceSoundEffects.py`。ゲーム中には実行しません。
- `analysis.json`: WAVから測定した長さ、peak/RMS dBFS、DC偏り、左右差。

2026-10-21 検証:

- 全19WAV: フォーマット、クリッピングなし、DC偏り、無音の両端、音量の検査に合格。
- Unity 6000.3.9f1の一時プロジェクトへ実際のWAV・音源読込・ミキサー・SE制御・検証コードをコピーし、`SoundEffectsValidation.RunAudioCore` に合格。音声以外のゲーム設定依存はスタブ化し、UI／ネイティブ出力検証は実行していません。
- Unity側で19WAVの実インポート設定、デコード、P1/P2分離、重複抑止、BGM／SE独立ミュート、ステレオ補間、モノラル変換、コールバック分割時のPCM一致、8音＋BGMのピーク抑制を確認しました。物理出力へは再生していません。
- Unity付属Roslynと既存プロジェクトの参照アセンブリを使い、この作業ツリーのゲーム本体と全Editorソースのコンパイルに合格（既存の非推奨API等の警告のみ）。

実際のレース進行、音楽・エンジンとの聴感バランス、USB/HDMI機器での再生確認は未実施です。
