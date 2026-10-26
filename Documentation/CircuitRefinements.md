# Tsukukoma Circuit：2026年10月26日 改善記録

作業の起点は最新main `1b170d6`。専用ブランチ `codex/circuit-driving-refinements` で作業し、元の作業領域のmain・他の開発者の変更は編集していません。

## 項目別の変更

|項目|修正内容と効果|検証|
|---|---|---|
|ロケット|着地後もジャンプ後60mの制限で約79km/hに留まる原因を解消。実接地で制限解除し、最高速度を240km/hに統一。飛行・踏切前の安全制限は維持。|実コンポーネントと接地レイで240km/h、踏切・飛行79.2km/h、効果終了時の落下速度維持を確認。|
|周回|道路幅だけで除外されていたバリア・路肩・内壁の走行も通過条件に含める。物理フレームでゴール越えを追跡し、薄いトリガーの取りこぼしを補う。|バリア走行・高所の高速ゴールを含む2周、逆走・往復・重複接触・テレポート・復帰を検証。順序付き16ゲートと走行距離条件を維持。4mの隣接レーン移動で24mの進捗を飛ばす短絡も不許可。|
|カーブのバリア|端付近で外へ切る意図的な入力を尊重し、補正の残りも即時解除。手放しで外へ流れるときの保護は残す。|左右対称、速度別操舵上限、接地・空中・切断、補正OFFを確認。|
|ジャンプ|38m以内の接地した人間の車に自然な助走を補助。縁を濃色の下地と太いアンバーで表示。|実車プレハブを停止から走らせる物理試験、操作待ち・切断・CPU待機時の非加速、描画を確認。|
|メーター|目盛りを短く細くし、速度・回転数・ブーストのバーを太くする。|両配色、720p/1080pのUnity描画、既存HUDの値・更新検証。|
|校舎|壁・屋根・窓枠・ガラスを低彩度で落ち着いた材質に変更。壁・屋根に控えめな粒状感。|同一視点の変更前後のUnity描画。元のモデルやColliderを変更しない。|
|トンネル接続|32mで実内壁の断面へ遷移し、位置・接線を共通化。旧入口バリア2枚の二重表示・衝突を無効化。広がる側は壁の足元まで舗装とColliderを追加。|4接合部で隙間0m・最大接線差0.049度、表示とColliderの44,418頂点が一致。路肩259点で支持を確認。|
|トンネル内側|元FBXのUVリングから局所的な面を再構成。周方向96分割、長手約1.25m間隔のC1補間、滑らかな内向き法線。発光模様も内向きの面に対応。|元FBXを保持。新しい表示と両面Colliderの一致、道路交差0、側壁・天井342点、傾き10状態、バンク±90度を検証。|
|チュートリアル|アイテムを進行方向18m先から55m先へ移動。|両者55m配置、実入力で加速・旋回・アイテム接触・完了音各1回・カウントダウン移行を確認。|

トンネルの接続と内壁は、同じ断面とColliderを使うため一つのコミットにまとめています。助走補助がCPUの待機判断へ干渉しない対策は追補コミットです。

## 検証の再実行

Unity 6000.3.9f1 の専用コピーで検証しています。次のEditorメソッドを `-batchmode -projectPath ... -executeMethod ...` で実行できます。

- `CircuitDrivingRefinementValidation.RunAll`：ロケット、周回、助走、ハンドル、路面、全周バリア、復帰、内壁駆動、カメラをまとめて実行（`-quit`付き）。
- `CircuitVisualRefinementValidation.RunMeters`：両配色・両解像度のメーター描画。
- `CircuitVisualRefinementValidation.RunSchool`：校舎の描画。
- `CircuitVisualRefinementValidation.RunTube`：接合・Collider・路肩検証と描画。
- `DrivingTutorialValidation.Run`：実際の練習走行からカウントダウンまで（`-quit`なし、終了は検証側が管理）。
- EditMode NUnit：`-runTests -testPlatform EditMode -testResults ...`。

全周の左右衝突検査は、屋外1,522点・内側2,287点で欠落0。接合の4点では屋根より手前にトンネル側面が始まるため、バリアとトンネルを組み合わせて確認します。元の地形・道路の線形は維持し、地形保持率100%、道路の折り返し・地形交差0です。

## 他ブランチの確認

`raymee/night-ennichi-visuals` は開始時・区切り・終了前に取得して確認し、`c0d6158` のままでした。トンネルUVの局所的な面構成などの差分を調査し、元の三角形が遠い断面を結んでいる点も確認しました。現在の入力・UI・物理の変更と競合するブランチ全体はマージせず、既存の景観と光る模様を保ちながら今回の形状へ合わせています。

## 描画資料

画像と接合・衝突検査のJSONは [Artifacts/CircuitRefinements](../Artifacts/CircuitRefinements) に保存しています。メーターは表示値を与えたUIフィクスチャ、校舎とコースは実際のSampleSceneを描画したものです。

## 実走行の結果・残課題

最終NUnitは169件すべて成功。総合物理検証も成功し、実車プレハブと実接地・両コンポーネントのFixedUpdateを使った制御付き物理試験でロケット240.00km/hを確認しました。

|実走行条件|P1 / P2|CPU|コース外・復帰・実行中エラー|
|---|---|---|---|
|アイテムあり高速 `7101`（実装 `8928f4f`）|P1完走 / P2は2周目残り126.7mで待機時間終了|1台完走、1台未完走|すべて0|
|初心者・上級 `6101`（実装 `8928f4f`）|低速P1は3385.1mで360秒制限 / 上級P2完走|1台完走、1台未完走|すべて0|
|5秒の切断・復帰 `6105`（実装 `8928f4f`）|両者2周完走|両者2周完走|すべて0|
|最終・アイテムなし高速 `7102`（実装 `be715fb`）|両者2周完走|1台完走、1台は終了時残り14.5m|すべて0|

車両の位置・速度・周回・結果を強制設定しない、製品入力ゲート経由の試走です。既知のコースを運転するエージェントであり、初めて遊ぶ小学生の理解度を実証する試験ではありません。通常の抽選では今回はロケットが出なかったため、ロケット240km/hは上記の専用物理試験で別途検証しています。

残課題は、接触で遅れた車の待機時間終了、低速モデルの時間切れ、CPUが人間の終了時に残る条件です。未完走を周回検証成功とは混同せず、記録を残しています。実機のハンドル・ペダルでの手触り、夜景を含む現場の見やすさと対象PCのフレームレート、Windows/実機向けの製品ビルドは未検証です。Unity内のスクリプトコンパイル・描画・EditMode・Play Modeは実行しました。

Play Modeの開始時には、既存のUnity Editor SearchDatabase由来のArgumentOutOfRangeExceptionが一度記録されています。開始後のレース集計のruntimeErrorsは全4条件で空です。

全レースの集計は `playtest-report.json` / `final-drive-report.json`、NUnitと物理試験の要約は `verification.json` / `regression-results.txt` に保存しています。詳細ログ・入力/位置のCSVは作業領域の `Logs/CircuitRefinements/` に残しています。

C1以上の補間形状を有限な三角形メッシュへ近似しています。接合の0.049度は隣接メッシュ辺による実測値です。

## 実装コミット


|コミット|メッセージ|
|---|---|
|`4c0a198`|fix(rocket): release jump speed cap on contact and reach 240 km/h|
|`d9fad92`|fix(laps): count legal wall routes and sweep fast finish crossings|
|`6feb4cc`|fix(steering): respect deliberate curve barrier riding|
|`821986e`|fix(jump): add safe run-up acceleration and high contrast ramp edges|
|`25aa0d6`|style(hud): thicken instrument bars and reduce dial ticks|
|`9126ddd`|fix(tutorial): place practice pickups 55 meters ahead|
|`f87420b`|style(campus): add subdued concrete and restrained school finishes|
|`a83caa7`|fix(tunnel): join smooth interior and barrier collision with tangent continuity|
|`8928f4f`|fix(jump): preserve CPU spacing and add combined driving regression suite|
|`be715fb`|fix(laps): reject nearby lane projection shortcuts and reuse road samples|

本書・最終の検証資料を追加するコミットは、この一覧の後に作成します。最終ハッシュはPR履歴と完了報告を参照してください。
