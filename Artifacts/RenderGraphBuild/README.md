# Render Graph / Build And Run

Unity 6000.3.9f1 / URP 17.3.0。互換モードを無効にし、Desktopの `URP_COMPATIBILITY_MODE` を除去。EditorとPlayerの `Renderer2DResources` の型レイアウト不一致を解消しました。

SampleSceneのMac Development Buildが成功。Editorでは4種類の出力サイズでUI倍率、カメラスタック全体の深度付きターゲット、リサイズ・再利用・解放を検証しました。

StandaloneではRender Graphを有効にしたままタイトル→車種選択→練習→レースを実行。物理画面が1台のため、P2を1920×1080のターゲットへ描画し、1280×720のメイン画面用バッファへのGPU転送処理を264フレーム実行しました。実際の物理2画面の同時出力は未検証です。試験シーンの「Display 2が未接続」という既知の診断を除き、描画エラー・managed例外はありません。

添付PNGはRender Graphで描いたP2ターゲットです。`validation.json` に検証条件を記録しています。`Racing > Validation > Display Outputs` と `Racing > Build > Mac Display Validation` で設定・ターゲットの検証と通常のSampleSceneビルドを再実行できます。一時的なStandalone試験コードは製品側には含めていません。
