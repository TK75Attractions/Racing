# Standalone rendering performance

Unity 6000.3.9f1 / URP 17.3.0、Apple M1で2026-10-14〜15に検証。
Render Graphを有効にしたまま、背景の描画量とSSAOの負荷を削減した。

## 原因と修正

- スタート地点には1本35万三角形の木が複数あり、2プレイヤーの描画で負荷が大きかった。`1009こうしゃチューブ.fbx`のMesh LODを生成し、シーン内の15本の木にLOD Selection Bias 2を設定。元のLOD0と物理形状は保持する。
- 全カメラ共通のSSAOは、UI・背景・空のカメラにも適用され、DepthNormalsの事前描画でジオメトリを再描画していた。`RaceAmbientOcclusion`でワールドを描くカメラに限定。既存のopaque depthから法線を復元し、After Opaque・半解像度で陰影を残す。
- Retina/4K出力のピクセル数が負荷を増やす。`RaceRenderBudget`はネイティブ出力を保ち、共通URP render scaleを調整する。初期値は最大0.8、各ビューの描画を概ね1920×1080に制限。ゲーム中のフレーム時間を平滑化し、負荷が高い時は0.05ずつ下げ、余裕が5秒続いたら上げる。下限0.5。VSync・明示的なFPS制限による待ち時間を考慮し、アプリ切替や100ms超の停止を無視する。

URPの共通render scaleはスタック内のUIにも適用される。低解像度での鮮明さとフレーム時間のトレードオフはある。表示サイズと16:9のレターボックスは維持する。

## 実測

Development Build、4台、VSyncなし。P1はネイティブ出力、P2は1920×1080の別RenderTextureで全カメラスタックを描画し、転送処理も実行。物理ディスプレイは1台のため、実モニター2枚での表示・同期は未検証。

| 条件 | 中央値 | p95 | メモ |
| --- | ---: | ---: | --- |
| 1080p・木LOD無効・scale 0.8 | 16.60ms | 18.31ms | 同一アプリ内の分離計測 |
| 1080p・木LOD有効・scale 0.8 | 10.86ms | 11.98ms | 同じ配置、約92fps |
| 1080p・60秒走行 | 12.31ms | 13.17ms | 約81fps、2人とも約1.2km走行 |
| 2880×1800・スタート地点 | 14.77ms | 17.16ms | scale約0.667、P2は別1080pターゲット |
| 2880×1800・60秒走行 | 11.76ms | 14.74ms | 約85fps、P1 1222m / P2 1204m |

上記はUnityがフォーカスありと報告したフレームの計測。1080pの走行は調整途中の記録、Retinaの走行は最終コードでの記録。1080pでのLOD比較は同じ実行内で描画以外の状態を維持している。

元の描画設定ではスタート地点の三角形数中央値が約870万、修正後は約150万。`before-1080.json`は30.55ms、`after-1080.json`は10.47msだが、前者はフォーカスなし、後者はフォーカスありとUnityが報告しているため、この2つの時間を厳密な改善倍率として扱わない。生データの`focusedFrames`と`focused-frame-ms`で区別できる。

最初の分離計測ではSSAOを外すと870万→440万三角形、背景メッシュを外すと約73万三角形になった。シミュレーションや影だけを止めた場合の改善は小さく、描画が主因と判断した。`Racing.Display.Present`はCPU側の転送コマンド発行時間を測るマーカーであり、GPU全体の転送時間を表すものではない。

## 検証と再実行

`Racing/Validation/Standalone Rendering`はRender Graph・互換defineの除去、画面サイズの異なるターゲットのリサイズ/再利用/破棄、UIスケール、SSAO設定、15本の木のLODを検証する。通常Macリリースビルドと計測用ビルドは成功。リリース版は起動ログまで確認し、端末ロックのため画面の目視確認は未実施。開発版の車・UI・木・Retinaレターボックスは保存画像で確認した。

```sh
# Unityを閉じて実行。BuildReleaseなら計測プローブを起動しない通常ビルド。
/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" \
  -executeMethod StandalonePerformanceValidation.Build -quit \
  -logFile "$PWD/Logs/StandalonePerformance/build.log"

open -n -W -a "$PWD/Builds/StandalonePerformance/Race.app" \
  --env "RACING_PERFORMANCE_OUTPUT=$PWD/Logs/StandalonePerformance/measurement" \
  --env RACING_PERFORMANCE_DRIVE=1 \
  --args -screen-fullscreen 0 -screen-width 1920 -screen-height 1080
```

プローブはDevelopment Buildかつ環境変数指定時だけ有効。外部送信を止め、実際のタイヤ・Rigidbodyにペダルとハンドル入力を与えて走行する。計測後は自動終了。フォーカスの有無を別記録し、レポートの`errors`とタイムアウトファイルも確認する。`BuildBaseline`は元のSSAO設定を一時的に使用し、ビルド後に設定ファイルとアプリIDを復元する。比較実行時は`RACING_PERFORMANCE_BASELINE=1`を指定してLOD0・scale 1を強制する。

生データと確認画像は`Artifacts/StandalonePerformance`に保存。

参考: [Unity Mesh LOD生成](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/lod/mesh-lod/mesh-lod-generate-lods)、[LOD品質設定](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/lod/mesh-lod/mesh-lod-quality)。
