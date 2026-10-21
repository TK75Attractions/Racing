# 見た目・音・画面体験の改善 64 項目の対応状況

作成日：2026年10月21日  
対象：[PresentationImprovementBacklog.md](PresentationImprovementBacklog.md) の 64 項目。基準の方向性は「夜の学校 × 縁日ネオン」（夜が初期値）。  
ブランチ：`raymee/night-ennichi-visuals`

## 比較画像と計測

同じ地点・車・時刻の定点撮影（`CourseVisualTour`、`RACING_VISUAL_TOUR_MODE=fixed`）。

| 画像 | 内容 |
|---|---|
| [before-day-contact-sheet.png](../Artifacts/PresentationBacklog/before-day-contact-sheet.png) | 着手前に相当する昼の見た目 |
| [night-first-pass-contact-sheet.png](../Artifacts/PresentationBacklog/night-first-pass-contact-sheet.png) | 夜化と不具合修正だけの段階 |
| [final-night-contact-sheet.png](../Artifacts/PresentationBacklog/final-night-contact-sheet.png) | 全項目の反映後（夜）。**01 の基準画像として使う** |
| [final-day-contact-sheet.png](../Artifacts/PresentationBacklog/final-day-contact-sheet.png) | 全項目の反映後（昼に切り替えた場合） |
| [final-02-school-front-moving.png](../Artifacts/PresentationBacklog/final-02-school-front-moving.png) ほか | 校舎前・チューブ内・スタートの原寸 |

フレーム時間（Windows の開発 PC、Snapdragon X Elite 内蔵 GPU、D3D11、1920×1080、P1 ネイティブ＋P2 オフスクリーン、自動運転の 1 レース）。**本番機ではないので、絶対値ではなく前後比較に使う。**

| 時点 | 中央値 | p95 | p99 | 最大 | 50ms 超 |
|---|---|---|---|---|---|
| 夜化の第 1 段（s1） | 50.0 | 100.0 | 133.3 | 1183.5 | 1219 |
| 開始時の停止対策・段階 B〜C（s4） | 41.4 | 56.6 | 62.9 | 78.1 | 494 |
| 段階 D・区画分割・反射プローブ（s5） | 38.3 | 52.0 | 67.6 | 131.6 | 188 |

最終版（画面まわり・UI 等倍・フォント・タイトルの反映後）は、計測中に PC が使用中でウィンドウが前面に来なかったため、比較できる数値が取れていない。本番機での計測（57）で確認する。

連続運営（64）：`RACING_PERFORMANCE_CYCLES=2` で 2 回連続のレースを無人で回し、GameObject・音源・RenderTexture の数は増えず、通知・設定の残留は 0（[cycles-2.json](../Artifacts/PresentationBacklog/cycles-2.json)）。

## 項目ごとの状況

「実装」＝コードと定点撮影で確認。「実装（未撮影）」＝コンパイルと検証は通ったが、定点撮影の構図に入らないもの。「ユーザー」＝実機・会場でしか確認できないもの。

| ID | 状況 | 主な変更 |
|---|---|---|
| 01 | 実装 | 夜の学校 × 縁日ネオンに統一。基準画像は上の final-night |
| 02 | 実装 | 路面の大きな色むら・補修跡・タイヤの通り道（`RaceCourseRoad`） |
| 03 | 実装 | 舗装の端と草の縁取りの帯 |
| 04 | 実装 | 縁石の天面・側面、バリアの立体レール・支柱・継ぎ目 |
| 05 | 実装 | チューブ壁を規則的なリング格子で作り直し、天井に細いリブ・壁に帯・路面際に矢印・区間番号 |
| 06 | 実装 | チューブ入口・出口のネオン門と反射ストライプ（`TubePortalVisualController`） |
| 07 | 実装（未撮影） | 区間ごとの固有の目印 4 か所（`NightEnnichiVisualBuilder`） |
| 08 | 実装 | 提灯の間隔と花びらの上限を絞り、遠景の層を追加 |
| 09 | 実装 | 月明かりと環境光の調整、縁石の弱い発光、車体下の発光と補助光 |
| 10 | 実装 | チューブの明暗のリズム、カーブ外側の強調 |
| 11 | 実装 | チューブ模様・桜の発光を弱め、同時の主役を 2 層までに |
| 12 | 実装 | 車の接地影 |
| 13 | 実装 | 区間ごとのベイク反射プローブ 3 基（夜でベイク） |
| 14 | 実装 | 影 40m・2 カスケード |
| 15・16 | 実装 | チューブ出入口の前後 12m で環境光・フォグを補間、夜のフォグは紺紫・低密度 |
| 17・18 | 実装 | 塗装・樹脂・金属・ガラス・ゴムの材質分離、後部とテールランプ（`CarMaterialTuningBuilder`） |
| 19 | 実装 | 1P・2P・CPU の札（形も変える。自分の画面では自車の札を隠す） |
| 20・21・22 | 実装（未撮影） | ホイールの見かけの回転補正と接地、車体の小さな傾き（表示用のみ） |
| 23 | 実装 | ブレーキ・後退・尾灯の発光を入力と運動に一致 |
| 24 | 実装 | 車種別の排気色とエンジンの響き |
| 25・27 | 実装 | 高速時のカメラの高さ・注視距離・FOV、カーブの先読み |
| 26 | 実装 | チューブ内のカメラ遮蔽回避 |
| 28 | 実装 | 速度線を外周へ、雷・花びらの中央視界保護 |
| 29・30 | 実装（未撮影） | 成功演出の形・位置の区別、煙と火花の量を速度・接触時間に合わせる |
| 31 | 実装（未撮影） | ゴール後のカメラの引きと回り込み |
| 32 | 実装 | 演出を弱くする設定（F8 の `RaceEffectsOperatorPanel`） |
| 33・34 | 実装 | HUD の情報と文字の階層を `RacingHUDStyle` に集約 |
| 35 | 実装 | UI を描画解像度に左右されず等倍で表示（`NativeResolutionUI`） |
| 36 | 実装 | ミニマップのマーカーの形・縁取り・前後関係 |
| 37・38 | 実装 | 通知を優先度つきキューに統合（`DriverNoticeQueue`）、急カーブ・逆走の案内を追加 |
| 39 | 実装 | 支援の種類を小さなアイコンとラベルで |
| 40 | 実装（未実行） | 解像度別の比較撮影（`RACING_VISUAL_RESOLUTIONS`） |
| 41 | 実装 | タイトル背景を実際の夜のコースから生成（`TitleBackgroundBuilder.RunBatch`）。**結果画面の背景（東京の夜景）は未対応** |
| 42〜45・48 | 実装 | 練習案内の下寄せとカーブ矢印、待機・接続待ちの区別、車の性能説明、結果の優先順位、観戦の残り猶予 |
| 46 | 実装 | HUD のアイコン・ラベル併用と車の札 |
| 47 | ユーザー | 会場の着座位置・モニターで文字の読みやすさを確認 |
| 49〜51・54〜56 | 実装 | 通知時のダッキング、滑り音・衝突音の区別、ゴール前後の音のつなぎ、同音の間隔とリミッター、会場音量プリセット |
| 52・53 | 実装＋ユーザー | トンネル反響の既定値と定位の診断ログ。実スピーカーでの確認はユーザー |
| 57 | 実装＋ユーザー | Windows の無人計測（平均・p95・p99・引っ掛かり・`frames.csv`）。本番 2 画面での計測はユーザー |
| 58 | 一部 | テクスチャのミップ・異方性は設定済み。白線と遠方ネオンの細線のちらつきは形状・シェーダー側の対応が必要 |
| 59・60 | 実装 | 学校・チューブの 100m 区画分割（`CourseRenderChunkBuilder`、`Restore` で元に戻せる）、テクスチャ予算 |
| 61 | 改善 | シェーダーの事前準備と生成の前倒し、チューブ模様の事前生成。開発 PC で最大 1183→78〜132ms |
| 62 | 実装 | 画質プリセット Low/Medium/High/Auto と負荷表示（F8） |
| 63 | 実装 | 定点撮影と昼／夜の土台／全部の比較（`CourseVisualTour`） |
| 64 | 実装＋ユーザー | 連続運営の無人確認（`RACING_PERFORMANCE_CYCLES`）。本番機での長時間運転はユーザー |

## 既知の問題と注意

- D3D12 では、起動直後に `d3d12: upload buffer was too small` の後でアプリが終了することがあった（開発 PC）。計測は `-force-d3d11` で行った。本番が macOS（Metal）なら影響しない見込みだが未確認。
- 既存の検証のうち、`MainIntegrationValidation`・`SoundEffectsValidation`・`CourseBarrierValidation` は、macOS 専用の音声出力プラグイン（RaceAudio）が Windows に無いために失敗する。`EngineAudioValidation` のトンネル内の判定地点 1 点は、今回変更していない当たり判定に対して古い座標を使っている。詳細は [EditorValidationFixes.md](EditorValidationFixes.md)。
- `CourseRenderChunkBuilder` で学校・チューブの表示を分割しているため、コース・チューブ・材質のビルダーを後から実行するときは、先に `CourseRenderChunkBuilder.Restore` を実行し、終わったら `Run` と反射プローブのベイクをやり直す（[StandalonePerformance.md](StandalonePerformance.md)）。
