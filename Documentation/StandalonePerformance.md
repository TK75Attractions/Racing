# Standalone rendering performance

Unity 6000.3.9f1 / URP 17.3.0、Apple M1で2026-10-14〜15に検証。
Render Graphを有効にしたまま、背景の描画量とSSAOの負荷を削減した。

レース音声の滑り音・衝突音・着地音は各プレイヤーの既存ミキサー内で処理し、追加のオーディオストリームを使わない。4台のエンジン音と2系統のBGM出力という既存構成を維持する。

2026-10-21: バックログ16の初期調整として `RaceEnvironmentController` の夜間フォグを紺紫・低密度（上限0.0006）にした。リアルタイムライトやカメラ数は増やしていない。2画面描画の計測と、ジャンプ先・急カーブ・チューブ出入口の目視確認はリード担当で未実施。

バックログ12・24で車両ごとの接地影と少量の排気パフを追加しました。各車1枚の影メッシュと、アクセル中最大約11粒子/秒の発生です。2カメラでの描画・CPU/GPU負荷は今回未計測です。次回の同一コース・同一画角の2画面計測で差を記録してください。

## 原因と修正

- スタート地点には1本35万三角形の木が複数あり、2プレイヤーの描画で負荷が大きかった。`1009こうしゃチューブ.fbx`のMesh LODを生成し、シーン内の15本の木にLOD Selection Bias 2を設定。元のLOD0と物理形状は保持する。
- 全カメラ共通のSSAOは、UI・背景・空のカメラにも適用され、DepthNormalsの事前描画でジオメトリを再描画していた。`RaceAmbientOcclusion`でワールドを描くカメラに限定。既存のopaque depthから法線を復元し、After Opaque・半解像度で陰影を残す。
- Retina/4K出力のピクセル数が負荷を増やす。`RaceRenderBudget`はネイティブ出力を保ち、共通URP render scaleを調整する。初期値は最大0.8、各ビューの描画を概ね1920×1080に制限。ゲーム中のフレーム時間を平滑化し、負荷が高い時は0.05ずつ下げ、余裕が5秒続いたら上げる。下限0.5。VSync・明示的なFPS制限による待ち時間を考慮し、アプリ切替や100ms超の停止を無視する。

F8運営パネルからLow / Medium / Highを選ぶと、render scaleを最大0.55 / 0.70 / 0.85に固定し、PlayerPrefsに保存する。Autoを選ぶと従来の追従動作（最大0.8、下限0.5）へ戻り、選択モードも保存される。固定値にも高解像度時の1920×1080相当の上限を適用する。2画面の実測で各プリセットの品質・負荷を比較する作業は未実施。

描画バックログ13・58・59・60には、明示実行するEditorビルダーを追加した（下記）。この編集ではUnityを起動していないため、シーンへの配置・生成アセット・インポート設定はまだ未適用。木15本の4段LODとSelection Bias 2は維持する。

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
$env:RACING_PERFORMANCE_CYCLES = '5' # 省略時は1回。1～100回。
$env:RACING_PERFORMANCE_OUTPUT = Join-Path $env:TEMP ('RacingPerformance\Run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$run = Start-Process -FilePath $env:RACING_PERFORMANCE_BUILD -ArgumentList @(
  '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080',
  '-logFile', ('"' + (Join-Path $env:TEMP 'racing-performance-player.log') + '"')
) -PassThru -Wait
$run.ExitCode
Get-Content (Join-Path $env:RACING_PERFORMANCE_OUTPUT 'report.json')
Get-Content (Join-Path $env:RACING_PERFORMANCE_OUTPUT 'cycles.json')
# 終了後、同じシェルから通常起動する前に解除する。
Remove-Item Env:RACING_PERFORMANCE_AUTORUN
Remove-Item Env:RACING_PERFORMANCE_CYCLES
Remove-Item Env:RACING_PERFORMANCE_OUTPUT
Remove-Item Env:RACING_PERFORMANCE_BUILD
```

Development Build の `RACING_PERFORMANCE_AUTORUN=1` だけで有効になる。出力先省略時は `Application.temporaryCachePath/RacingPerformance/<UTC日時>`。環境変数がなければ通常プレイのまま。Editor・非 Development Build では自動実行しない。旧 `RACING_PERFORMANCE_DRIVE` / `BASELINE` / `SINGLE_SAMPLE` はこのモードでは使わない。

タイトル → 車選択（保存済みの選択を確定）→ 練習画面 → カウントダウンを既存の遷移で進める。各準備画面には約 3 秒ずつ滞在し、練習課題は既存の管理用スキップ経路で終了する。入力初期化時にシリアル・シミュレータを選ばずキーボードを用意するので、シリアル機器は不要。CPU 2 台はシーン設定どおりの通常ドライバー。自動サイクルは結果画面で両プレイヤーをタイトルへ戻し、レースの完了または時間切れを待つ。外部 Firebase 送信は既存プローブと同様に停止する。

`RACING_PERFORMANCE_CYCLES=N` はこの自動進行を N 回繰り返す（1～100、未指定・不正値は1回）。各回とも実際の結果画面から両プレイヤーをタイトルへ戻して次組を開始する。`cycles.json` は各回のタイトル復帰後に追記保存し、Total Used Memory、Mono heap、GameObject、AudioSource、Texture、RenderTexture 数と前回との差分、残留ドライバー通知数、準備ルールのロック／予約状態、現在の機能・レース設定を記録する。3回以上で3点が単調増加したメモリ／オブジェクト数と、残留通知・ルール状態は `warnings` に記録し、完了時にPlayerログにも警告する。メモリはGCを強制せず採取するので、単調増加はリーク確定ではなく調査対象。プローブはDevelopment Build専用で、各周回は最大600秒で打ち切る。

### 本番機での連続運転（ユーザー実施）

本番の2画面・2音声機器では、まず通常起動でP1/P2それぞれの画面と音声出力先を割り当て、両方からタイトル音・エンジン音・BGMが出ることを確認する。音声機器を抜き差しした場合はゲームを再起動して機器名と出力先を再確認する。次に実機入力でタイトルから準備、練習、レース、結果を複数組続け、各画面の遷移、通知の消去、音量・画質プリセット・レースルールの次組への戻りを記録する。意図した音量／画質プリセットは次組後も保持される設定として確認する。

自動周回は上記のDevelopment Build手順で別途実行し、同じ解像度・2画面接続・音声機器接続で `cycles.json` とPlayerログの `PERFORMANCE_CYCLE_LEAK_SUSPECT` を保存する。自動モードはキーボード入力源を使うため、本番機器の接続・出力の実運用確認を代替しない。2画面を1台の物理ディスプレイにオフスクリーン合成した結果と、本番の独立2画面表示は別条件として扱う。

P1 はネイティブ表示、P2 は既存の RenderTexture スタックを使用する。物理画面が 1 枚の場合は既存プローブと同様に P2 を 1920×1080 のオフスクリーン出力で描画・転送する。`report.json` の `outputMode`、画面サイズと機器情報を比較条件として保存する。自動レースでは本番の `RaceRenderBudget`・vSync・FPS 設定を維持するため、上記の旧 Mac 計測値（vSync 無効）とは条件が異なる。

出力の読み方:

- `report.json` の `race`: `meanMs` / `medianMs` / `p95Ms` / `p99Ms` / `maxMs`、`over33_3ms` / `over50ms`、`first10SecondsMaxMs`。ウォームアップを捨てず、レース開始を観測したフレームから次のフレーム境界で記録する。準備・カウントダウンの時間は統計に含めない。中央値は中央 2 値の平均、p95/p99 は nearest rank。最初の 10 秒に重なるフレームは初回最大値の対象。
- `frames.csv`: レース開始からの経過秒、フレーム時間 ms、P1 速度 m/s、コース開始点からの位置 m（周回時に戻る）、世代別 GC 回数差分、GC Alloc bytes。座標・速度は観測時点の値で、GC 差分は前回観測以降。小数点は常に `.`。フレーム時間は `Time.unscaledDeltaTime` のため、vSync・待ち時間も含む。
- `race.spikes`: 50ms 超の時刻・位置・速度・GC 差分と、その時点および直前フレームの ProfilerRecorder 値。時間カウンターは ms、GC Alloc / メモリは bytes、描画数・オブジェクト数は個数。`availableCounters` に実機で利用可能なものだけを列挙し、未対応カウンターは省略する。CSV の GC Alloc `-1` は取得不可。
- GC Alloc、Game Object Count / Object Count、Total Used Memory の前後差で生成・メモリ増加の手がかりを得る。これらは生成されたオブジェクトの名前や呼び出し元を特定するものではない。原因の確定には該当時刻・コース位置で Unity Profiler の追加採取が必要。
- `endReason`: `all-finished` / `race-timeout-or-dnf` / `240-second-limit`。240 秒終了は未完走でも正常な計測終了。レース未到達・0 フレーム・収集した Unity エラーがあれば終了コード 1、その他は 0。`errors` と `status.txt` も確認する。

CSV はメモリに蓄積して終了後に書き、レース中の毎フレームの文字列整形・ディスク I/O を避ける。遅いフレームの診断スナップショットには小量の割り当てがあり、その影響が次のフレームに現れる可能性がある。強制終了・クラッシュでは最終ファイルは保証しない。バックグラウンドでも動くが、比較時はフォーカス・解像度・画面枚数を揃え、`focusedFrames` も確認する。

この追記時点では Unity の Windows ビルド・実行は未検証。リードが実機で、無入力遷移、両プレイヤーの移動、2 本の描画スタック、完走／240 秒終了、JSON / CSV の生成と終了コードを確認する。

## 2026-10-21: 開始付近の停止調査（57 / 61）

対象は lead の `P61/perf-s1`（Windows Development / D3D11、Adreno X1-85）。CSV は起動からでなく GO 後の秒数。0–10秒は最大133.34ms、10–25秒は1183.47ms、25秒以降は200.00ms。50ms超は各115/145、104/169、1000/1730フレームで、長い停止の集中と、全体的な50ms前後の負荷は区別する。最大値は21.125516秒、速度0.0062m/s、距離1790.527m、GC差分は全世代0。17–22秒の主要スパイクでは Game Object Count=2944 / Object Count=18961 が一定。最大停止の観測カウンターは Main Thread=72.77ms / CPU Main Thread Frame Time=57.79ms、直前は81.97/141.38msで、1183ms全体を説明できない。GPU Frame Time=0 は GPU 待ちがない証明ではない。

`player.log` は車4台の参加→P1/CPU1リセット→初周通過の順。時刻付きログがないため、個々のリセットをCSVの秒に割り当てることはできない。CSV冒頭は約58mで既に移動中なので、1790mを開始前グリッドとする解釈は採用しない。初回コンパイル、物理の追い付き、GC、OS/ドライバー停止のどれか一つに原因を確定する材料は不足している。

コード上のタイミングと対処:

- 行灯は `OnEnable`、道路・進路マーカー・設置アイテムはシーン読み込み、夜の遠景リングは最初の `LateUpdate/ApplyEnvironment`（512三角形）で生成。夜の初期値はシーンの `timeOfDay: 1` を維持。行灯の `OnValidate`→`OnEnable`→最初の`Update`での重複再生成を抑制した。
- 車4台は `StartGameWhenScreenCovered` で生成し、カウントダウン後の GO は運転再開・表示・音の切替。車の煙、衝突火花、ドリフト火花を初回発生時から車両の `Awake` へ前倒し（車自体の生成は遮蔽中のまま）。`CarNightGlow` の64×64マスクはタイトル前に1回作り全車共有。タイヤ痕の固定バッファと雷の合成音はタイトル前に準備する。雷は既存の音量0の公開APIで合成のみ行い、音は再生しない。
- 花びらはカメラ検出時に生成。雷の線・ライトのプールは初回使用で増える。両者およびHUDは別タスクの編集範囲につき変更なし。SEは `RaceSoundEffects.Awake` で全cue合成、BGMは `RaceBackgroundMusic.Awake` でロード済み。車のエンジンは `EngineAudioCore.OnEnable` で初期化。最大スパイクとこれらの実行時刻の一致は未確認。
- `RaceStartupWarmup` は `Resources/RaceStartupVariants.shadervariants` をタイトル前に `WarmUp`。20シェーダー/125 variantsで、Resourcesの演出・環境Sky/Rain・URP Lit/Unlit/Particles Unlitを参照し、使用材質のキーワードと夜のFOG_EXP2・追加ライト・cascade shadowを含めた限定集合。全URP組合せやフォールバック専用の旧Standard等を網羅するものではない。Resources参照によりビルドへ含める。起動ログ `RACE_STARTUP_WARMUP` の件数・warmedを確認し、材質やURP設定変更時は集合を更新する。

リードによる確認: 上記Windows手順の引数に `-force-d3d11` を追加し、同じ解像度・2本のスタック・フォーカス条件で新規プロセスを計測する。初回とキャッシュ済みを分け、GO前の読み込み時間も記録する。`Racing.Startup.Warmup` が準備中に終わり、開始後の `Shader.CreateGPUProgram` / `CreateGraphicsGraphicsPipelineImpl`、`GC.Alloc`、`Physics.Simulate` / FixedUpdate回数、OS側CPU/ドライバー待ちを17–22秒で採取する。最初の10秒だけでなく10–25秒を比較し、`CourseVisualTour` と既存 `VisualEffectsValidation.Run` / `LightningValidation` / Standalone Renderingで見た目・構成も確認する。本編集ではUnity起動・ビルド・検証メニュー・撮影・性能再計測は未実施。

D3D12の `upload buffer was too small` は提示されたD3D11ログにはなく、原因未確定。候補は大きなモデル/チューブ/道路の頂点・index・テクスチャの一括転送、起動時static batching、AdrenoドライバーとUnity D3D12 backend。15本の木は描画LODを軽くしてもLOD0や同居するLODデータのロード・転送がなくなるとは限らない。遠景リングは小さく、巨大転送の第一候補ではない。今回の行灯二重生成抑制は不要な再転送を減らすがD3D12修正済みとはしない。D3D11を比較基準とし、リードはD3D12の完全ログ/クラッシュ情報を保存し、木→チューブ→static batchingを一つずつ除外したビルドで切り分ける。該当が確定したらモデルLOD0の別アセット化/軽量化、メッシュの区間分割、転送の分散を検討する（物理用メッシュや既存15本LOD検証を無断変更しない）。D3D12は実際の頂点形式と2画面HDRターゲットを含む `GraphicsStateCollection` を実機で採取し、読み込み中に温める必要がある。SVCだけでPSOの停止解消は保証しない。

公式資料: [Unity 6000.3 SVC.WarmUpのAPI別制限](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ShaderVariantCollection.WarmUp.html)、[PSOの読み込み中の事前準備と確認マーカー](https://docs.unity3d.com/6000.3/Documentation/Manual/shader-prewarm.html)。

## 2026-10-21: 反射・テクスチャ予算・区間カリング（13 / 58 / 59 / 60）

Unityを閉じ、保存済みのプロジェクトに対して次の各メソッドを個別に実行する。メニューは `Racing > Rendering`。共通引数は `-batchmode -projectPath "<project-path>" -executeMethod <method> -quit -logFile "<log-path>"`。ベイクにはグラフィックスが必要で、`-nographics` を付けない。

| 順序 | method | 処理 |
| --- | --- | --- |
| 1 | `RaceTextureBudgetBuilder.Run` | 車体最大2048、ホイール1024、共通カーボン512、路面Albedo2048／Height1024。ミップ・Trilinear・異方性4（路面8）・HQ圧縮、Crunchなし。DefaultとStandaloneに設定し、法線種別・sRGB・Alpha・Wrap・他プラットフォーム設定を保持 |
| 2 | `CourseRenderChunkBuilder.Run` | 学校FBXインスタンスの表示を100 m XZセルの独立メッシュへ分割。`Assets/Art/RenderBudget/Chunks`に保存。地形の既存加工済みメッシュも現在の形状を入力にする |
| 3 | `CourseReflectionProbeBuilder.Run` | スタート100×30×100 m、校舎前140×50×120 m、チューブ100×50×100 mの局所プローブを配置。各128 HDR、Baked、8 mブレンド。再配置は既存ベイク参照を維持するが移動後は再ベイクが必要 |
| 4 | `CourseReflectionProbeBuilder.Bake` | 配置も実行したうえで、保存された夜の環境を一時適用し、3基を同期ベイク。`Assets/Art/RenderBudget/Reflections/*Night.exr`を参照して保存。ライト／RenderSettings／一時Skyはベイク後に復元する |

これらはEditorコードのみ追加した段階で、実行・Unityコンパイル・撮影・計測は未実施。追加5ファイルはローカルのUnity参照DLL／既存ScriptAssembliesに対する外部C#コンパイルで成功（Unityの再インポート・実行を保証するものではない）。生成物とシーン差分はリードが確認して採用する。ベイクを含めリアルタイムライトは追加しない。木15本のLODは変更しないので、`StandalonePerformanceValidation.Validate` の既存期待値（4段以上・Bias 2）も変更不要。`TubeCourseValidation.Run` の元チューブMeshFilter／Collider参照も維持する。

カリングの安全範囲: 不透明・大型・非LOD・非LightmapのMeshRendererのみ。三角形を重心のセルへ丸ごと振り分けるので境界の頂点を動かさず、元の全頂点ストリームとサブメッシュをコピーする。2〜48セル、最大セルの三角形85%以下、合計頂点増加1.5倍以下の場合に採用し、それ以外は元Rendererを残す。細分化後のboundsは実際の三角形全体を包むため、長大な三角形のboundsはセルより広くなる。元GameObject・MeshFilter・Colliderを保持し、元Rendererだけを無効にする。子の `CourseRenderChunks` が元の有効状態を保存。再実行はこの所有子だけを置き換える。

復元は同じ引数で `-executeMethod CourseRenderChunkBuilder.Restore`。元Rendererの有効状態を復元し、生成した表示子を除去する。生成アセットは再利用のため残す。コース／チューブ／材質ビルダーを後から実行する場合は先にRestoreし、完了後にRun→ベイクを再実行する。分割アセットは最新の元メッシュ・材質へ自動追従しない。中断・例外時のシーンは保存せず開き直すが、書込み済みの生成アセットは残り得るため再Runで揃える。

計測対象: 同じ夜・車・地点・F8固定プリセット・2本のスタックで、Restore時とRun時のCPU/GPU時間・p95/p99・三角形数・Draw Calls・メモリを比較する。Draw Calls増加や小さい効果ならRestoreして採用しない。チューブの別生成発光オーバーレイは今回分割しておらず、その負荷は残る。フラスタム単位を細かくするだけなので、学校越し／曲がりの先の完全な遮蔽カリングは保証しない。静的Occlusionデータを使用している場合は再ベイク・確認が必要。

58の残り: 道路白線・NeonRaceMarkersはテクスチャを使わない形状、チューブの発光はシェーダー。ミップ／異方性の対象にはできない。将来の看板・道路材質が参照するインポート可能なテクスチャは最大1024・異方性4を適用（UIは除外）。現在の路面手続き生成128×128テクスチャはインポーターの対象外で、他タスクの `RaceCourseRoad` は変更していない。`WORLD_TEXTURE_BUDGET` ログで実対象を確認する。PC Qualityの異方性は既存で有効。細線、遠方ネオン、木のLOD切替、各プローブ範囲のつながりは `CourseVisualTour.RunBatch` と実走で確認する。昼・雨は夜ベイクのままなので別途評価が必要。

API根拠: [Editorで非Readableメッシュを読むMeshUtility](https://docs.unity3d.com/ja/current/ScriptReference/MeshUtility.AcquireReadOnlyMeshData.html)、[同期反射ベイク](https://docs.unity3d.com/jp/current/ScriptReference/Lightmapping.BakeReflectionProbe.html)。

### 2026-10-21: 環境仕上げの再計測（04・07・08・15・16）

リードが `NightEnnichiVisualBuilder.Run` を実行後、上記と同じ2本のカメラスタックで比較する。追加は各バリアの不透明結合フレーム1描画、区間目印4結合メッシュ、遠景512三角形1描画/画面。ライト・シャドウパス追加なし。提灯は64 m間隔、花びら上限1800/カメラへ削減。チューブ区間の物理判定はビルダーで焼き込み、実行中は各Baseカメラのコース進捗と短距離補間のみ（OverlayはBaseの補間値を継承）。

p95/p99、開始時の生成・転送スパイク、draw call/SetPass/透明描画、GCを入口・出口・時計塔・ジャンプ前で記録する。新規 `Environment Solid` と `Night Skyline Depth` の初回シェーダー使用も確認する。P1/P2を屋外と内部に分けた画像で色の漏れを確認し、`CourseVisualTour` と既存道路／バリア検証を併用する。今回Unity・撮影・性能計測は未実施、負荷改善の実測値は未確定。

## 2026-10-21: 等倍UI合成の費用（35 / 40）

`NativeResolutionUI` は各プレイヤーに出力サイズのLDRカラー＋24-bit深度（通常D24S8）の中間RTを1枚追加する。Color 4 B＋Depth/Stencil 4 Bを仮定すると720pで約7.03 MiB/人（2人14.06）、1080pで15.82 MiB/人（31.64）、1440pで28.13 MiB/人（56.25）。既存P2転送RT、URPの一時HDR／GBuffer／ポスト処理RTは別で、この表は総使用量ではない。実際の深度形式・アラインメント・URP一時RTで増減する。RTは同寸法なら再利用し、リサイズ／無効化で解放する。キャプチャが違う寸法を要求すると作り直すので、撮影中の割当量を通常走行の値として扱わない。

毎フレーム追加は各画面の全画面RawImage 1描画（2三角形）と合成先の書込み。UIカメラ自体は既存の1台をOverlayからBaseへ移し、追加の追従カメラ・3D再描画・リアルタイムライト・影を増やさない。UIはネイティブ解像度で描くため、そのピクセル数はscale 0.55時の従来比で約3.31倍。独立Baseのセットアップ、ネイティブ解像度の深度／Renderer機能、既存Deferred/SSAO等の処理費用も残る。P2の最後の転送回数は従来どおり。平均ms／改善量は未計測で、負荷低下とは主張しない。

リード: 同一夜・地点・2画面・固定Low/Medium/Highで、従来版と本変更のCPU/GPU、p95/p99、Draw Calls/SetPass、RenderTextureメモリ、GCとリサイズ直後の最大msを比較する。Frame Debuggerで3D縮小→UI 1x→P2転送の順、UIのBloom/AA不適用と二重描画なしを確認する。片方720p・片方1440pも実施し、サムネイル撮影後にrenderScale／両画面RTが復元されることを確認する。通常走行の既存 `StandalonePerformance` 手順を使い、撮影ジョブは性能計測に用いない。コードのコンパイルのみ確認、Unity起動・撮影・性能計測は未実施。