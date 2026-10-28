# Windows x64対応

2026-10-27確認。Unity 6000.3.9f1。Windows用の描画・ESP32 COMポート入力・WASAPI音声・管理画面中継は既存実装があります。Windows版の2人表示は2台以上の拡張ディスプレイが必要です。Mac用PTYシミュレーター、AppKitの1画面2ウィンドウ・外付け自動全画面はWindowsの機能ではありません。

## 今回の変更

- 保存済みSampleSceneのP2指定はDisplay 3。PCに2画面しかない場合はDisplay 2へ切り替え、画面番号をログに残します。Editorの仮想DisplayとMacのネイティブ表示は維持します。
- `Racing > Build > Windows x64` でSampleSceneを `Builds/Windows/Race.exe` へ作成します。Mono／.NET Framework互換を指定します。
- Windows x64ビルド前に音声DLLの存在・PE x64ヘッダー・必要なAPI名・インポート対象を検査します。Build Profilesからのビルドにも適用します。
- `Tools/Build-Windows.ps1` はWindows上で音声DLLを再生成してからUnityバッチビルドを実行します。スペース入りパスに対応します。
- `Native~/RaceAudio/build-windows.cmd` はVisual Studioのx64 C++環境を検索し、静的ランタイムでDLLをビルドします。一時フォルダーでコンパイル成功後にAssetsへコピーします。

## ビルド

Unity Hubで6000.3.9f1のWindows Build Support (Mono)を追加。WindowsにはVisual Studio／Build ToolsのC++デスクトップ開発・Windows SDKが必要です。Unityとゲームを閉じ、プロジェクトのルートから実行します。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Build-Windows.ps1
```

Unityの場所が違う場合は `-UnityEditorPath 'D:\Unity\Editor\Unity.exe'` を指定。ログは `Logs/WindowsBuild.log`。`Builds/Windows` 全体を配布してください。管理画面にはプロジェクトのToolsと.NET 10 SDKも別途必要です。

## 検証範囲と制限

この作業環境はMacで、Windows Build SupportとWindows実機がありません。Windows用C#のコンパイルと表示先の単体テストを行いますが、Windows exeのビルド・起動・物理2画面・COM機器・WASAPIでの聴取は実機確認が必要です。

同梱Windows音声DLLはx64ですが、`ra_set_min_buffer_milliseconds` と `ra_get_underruns` が未収録です。このMac環境ではDLLを再生成できないため、上記Windowsスクリプトで現在のC++ソースから更新してください。旧DLLのままのビルドは事前チェックで防ぎます。DLL再作成後もEditorの再起動が必要です。

受入チェックは [PC・Unity操作マニュアル](CustomerServiceManual.md) の「起動後の動作確認」を使用してください。

公式資料：[Unity 6.3 Windows要件](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/windows/requirements-and-compatibility)／[Windows設定](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/windows/playersettings)／[コマンドラインビルド](https://docs.unity3d.com/kr/6000.0/Manual/build-command-line.html)
