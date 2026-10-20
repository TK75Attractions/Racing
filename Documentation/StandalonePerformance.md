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

## Windows: 無人で 1 レースを計測（項目 57 / 61）

Unity 6000.3.9f1 の Windows Build Support を用意する。Unity Editor を閉じ、保存済みのプロジェクトから PowerShell でビルドする。`BuildWindows()` はシーンを開いたり保存したりせず、描画アセット・PlayerSettings も変更しない。Development Build / StandaloneWindows64 / SampleScene を使用する。

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe'
$project = (Get-Location).Path
$env:RACING_PERFORMANCE_BUILD = Join-Path $env:TEMP 'RacingPerformance\Windows\Race.exe'
& $unity -batchmode -projectPath $project -buildTarget Win64 `
  -executeMethod StandalonePerformanceValidation.BuildWindows -quit `
  -logFile (Join-Path $env:TEMP 'racing-performance-build.log')
```

`RACING_PERFORMANCE_BUILD` は `.exe` パス、または出力フォルダ。未指定ならプロジェクト外の一時フォルダに一意のサブフォルダを作る。実際の出力先はビルドログの `STANDALONE_PERFORMANCE_BUILD_PASS` で確認する。Mac 用の既存 `Build()` は従来どおり。

```powershell
$env:RACING_PERFORMANCE_AUTORUN = '1'
$env:RACING_PERFORMANCE_OUTPUT = Join-Path $env:TEMP ('RacingPerformance\Run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$run = Start-Process -FilePath $env:RACING_PERFORMANCE_BUILD -ArgumentList @(
  '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080',
  '-logFile', ('"' + (Join-Path $env:TEMP 'racing-performance-player.log') + '"')
) -PassThru -Wait
$run.ExitCode
Get-Content (Join-Path $env:RACING_PERFORMANCE_OUTPUT 'report.json')
# 終了後、同じシェルから通常起動する前に解除する。
Remove-Item Env:RACING_PERFORMANCE_AUTORUN
Remove-Item Env:RACING_PERFORMANCE_OUTPUT
Remove-Item Env:RACING_PERFORMANCE_BUILD
```

Development Build の `RACING_PERFORMANCE_AUTORUN=1` だけで有効になる。出力先省略時は `Application.temporaryCachePath/RacingPerformance/<UTC日時>`。環境変数がなければ通常プレイのまま。Editor・非 Development Build では自動実行しない。旧 `RACING_PERFORMANCE_DRIVE` / `BASELINE` / `SINGLE_SAMPLE` はこのモードでは使わない。

タイトル → 車選択（保存済みの選択を確定）→ 練習画面 → カウントダウンを既存の遷移で進める。各準備画面には約 3 秒ずつ滞在し、練習課題は既存の管理用スキップ経路で終了する。P1 / P2 は `PlayerAutodrive` を既存入力ゲート経由で使用する。入力初期化時にシリアル・シミュレータを選ばずキーボードを用意するので、シリアル機器は不要。CPU 2 台はシーン設定どおりの通常ドライバー。計測モードだけは人間 2 人の完走後も CPU を待ち、全員完走、レースの時間切れ、または起動から 240 秒で保存・終了する。外部 Firebase 送信は既存プローブと同様に停止する。

P1 はネイティブ表示、P2 は既存の RenderTexture スタックを使用する。物理画面が 1 枚の場合は既存プローブと同様に P2 を 1920×1080 のオフスクリーン出力で描画・転送する。`report.json` の `outputMode`、画面サイズと機器情報を比較条件として保存する。自動レースでは本番の `RaceRenderBudget`・vSync・FPS 設定を維持するため、上記の旧 Mac 計測値（vSync 無効）とは条件が異なる。

出力の読み方:

- `report.json` の `race`: `meanMs` / `medianMs` / `p95Ms` / `p99Ms` / `maxMs`、`over33_3ms` / `over50ms`、`first10SecondsMaxMs`。ウォームアップを捨てず、レース開始を観測したフレームから次のフレーム境界で記録する。準備・カウントダウンの時間は統計に含めない。中央値は中央 2 値の平均、p95/p99 は nearest rank。最初の 10 秒に重なるフレームは初回最大値の対象。
- `frames.csv`: レース開始からの経過秒、フレーム時間 ms、P1 速度 m/s、コース開始点からの位置 m（周回時に戻る）、世代別 GC 回数差分、GC Alloc bytes。座標・速度は観測時点の値で、GC 差分は前回観測以降。小数点は常に `.`。フレーム時間は `Time.unscaledDeltaTime` のため、vSync・待ち時間も含む。
- `race.spikes`: 50ms 超の時刻・位置・速度・GC 差分と、その時点および直前フレームの ProfilerRecorder 値。時間カウンターは ms、GC Alloc / メモリは bytes、描画数・オブジェクト数は個数。`availableCounters` に実機で利用可能なものだけを列挙し、未対応カウンターは省略する。CSV の GC Alloc `-1` は取得不可。
- GC Alloc、Game Object Count / Object Count、Total Used Memory の前後差で生成・メモリ増加の手がかりを得る。これらは生成されたオブジェクトの名前や呼び出し元を特定するものではない。原因の確定には該当時刻・コース位置で Unity Profiler の追加採取が必要。
- `endReason`: `all-finished` / `race-timeout-or-dnf` / `240-second-limit`。240 秒終了は未完走でも正常な計測終了。レース未到達・0 フレーム・収集した Unity エラーがあれば終了コード 1、その他は 0。`errors` と `status.txt` も確認する。

CSV はメモリに蓄積して終了後に書き、レース中の毎フレームの文字列整形・ディスク I/O を避ける。遅いフレームの診断スナップショットには小量の割り当てがあり、その影響が次のフレームに現れる可能性がある。強制終了・クラッシュでは最終ファイルは保証しない。バックグラウンドでも動くが、比較時はフォーカス・解像度・画面枚数を揃え、`focusedFrames` も確認する。

この追記時点では Unity の Windows ビルド・実行は未検証。リードが実機で、無入力遷移、両プレイヤーの移動、2 本の描画スタック、完走／240 秒終了、JSON / CSV の生成と終了コードを確認する。
