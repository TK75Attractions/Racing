# 面白さとグラフィックの改善提案

作成日：2026年10月20日  
前提：`main`（59bb960）と、未マージの `origin/codex/racing-gameplay-features`（スリップストリーム、着地報酬、チューブのチャージライン、ドリフト案内、プリセット、結果の実績表示）。  
位置付け：提案のみ。ゲーム本体は変更していない。[RacingFeatureResearchPlan.md](RacingFeatureResearchPlan.md)（10/18）の続きで、そこで扱った機能は重複して提案しない。

## 1. 結論

- **面白さの不足は「仕組みの数」ではなく「見せ方」の問題が大きい。** 走行の仕組み（ドリフト加速、アイテム、スリップストリーム、着地報酬、チューブのライン）は既にそろっている。足りないのは、相手との接戦や成功の瞬間が、本人と観客に**伝わる演出**。
- **グラフィックの不足は「アートの方向性が決まっていない」ことが根本。** タイトルは「夜の首都高×ネオン」なのに、走るのは「昼の灰色の学校」。チューブ内のネオンと桜は雰囲気があるが、屋外は白・灰色の箱の校舎、薄い空、六角形のバリアで仮組みに見える。
- **すぐ直せる見た目の不具合が 3 つある。** スタート直後の木が白い破片の塊に見える、高速時に自分の車がブラーでにじむ、チューブの壁が昼の環境光で黄土色になる。
- **推奨：「夜の学校 × 縁日ネオン」に統一する。** 夜にすると、テクスチャの無い校舎を「窓明かり・提灯・ネオンの縁取り」で見せられるので、モデルを作り直さずに見栄えを大きく上げられる。チューブ内の既存の表現とも、タイトルの「ENNICH（縁日）CIRCUIT」とも合う。

## 2. 現状の診断

### 見た目（`main` をビルドし、実際に走らせて撮影。1280×720 のウィンドウ表示）

撮影した画面は [Artifacts/FunAndVisualProposal/](../Artifacts/FunAndVisualProposal/) にある。

| 場所 | 状態 | 画面 |
|---|---|---|
| タイトル | 夜の首都高・高層ビル・GT-R のイラスト。完成度は高いが、**実際に走るのは昼の学校**で、期待と中身が食い違う | [title.png](../Artifacts/FunAndVisualProposal/title.png) |
| スタート直後 | 右側の木が、**白い破片の塊**のように描画されている（葉の透過が効いていない可能性）。画面で最初に目に入る | [start-foliage.png](../Artifacts/FunAndVisualProposal/start-foliage.png) |
| 屋外全般 | 昼の薄い水色の空で、全体にコントラストが低く灰色っぽい。校舎は白・灰色の箱。六角形の半透明バリアが左右を覆い、画面がうるさい。アイテムボックスは無地のピンクの立方体 | [outdoor-barriers.png](../Artifacts/FunAndVisualProposal/outdoor-barriers.png)、[practice.png](../Artifacts/FunAndVisualProposal/practice.png) |
| チューブ内 | シアン・紫のネオンの矢印とリングは良い。ただし昼の環境光で壁が**黄土色**に照らされ、ネオンが映えない | [tube-motion-blur.png](../Artifacts/FunAndVisualProposal/tube-motion-blur.png) |
| 自車 | 高速時、**自分の車がモーションブラーで大きくにじみ、ギザギザ**になる。画面の主役がいちばん見づらい | [tube-motion-blur.png](../Artifacts/FunAndVisualProposal/tube-motion-blur.png)、[outdoor-barriers.png](../Artifacts/FunAndVisualProposal/outdoor-barriers.png) |
| ジャンプ前のブースト | 稲妻と紫の光が出て、手応えは伝わる | [jump-boost.png](../Artifacts/FunAndVisualProposal/jump-boost.png) |
| HUD | 順位・時間・ミニマップ・速度計はまとまっていて読みやすい | 全画面 |

描画は URP。Bloom、Neutral トーンマップ、モーションブラー、ブースト時の色収差・歪み・ビネット、速度に応じた FOV とスピードラインは既にある（[VisualEffects.md](VisualEffects.md)）。**後処理の効果は十分あり、足りないのは「画面に映る物」の質と統一感。**

### 面白さ

- 自動試走ではスリップストリームの発動が 0 回だった記録がある（機能ブランチの `Artifacts/GameplayFeatures/README.md`）。仕組みはあっても、プレイヤーが狙う状況になっていない可能性がある。
- 相手との差は順位と「+0m」程度の表示だけ。並走、抜いた瞬間、僅差のゴールといった、対戦がいちばん盛り上がる瞬間を強調する演出が無い。
- 観客が状況を読める手段が無い（各画面は本人視点のみ）。
- 人による試遊（理解度・楽しさ）はまだ取っていない。

## 3. アートの方向性（ユーザーが選ぶ）

| 案 | 内容 | 利点 | 欠点 | 参考 |
|---|---|---|---|---|
| **A. 夜の学校 × 縁日ネオン（推奨）** | 夜空。校舎は窓明かり・ネオンの縁取り・提灯で見せる。屋台・垂れ幕・花火 | 既存モデルのまま大きく化ける。チューブ・タイトルと一貫。ネオンは Bloom と相性が良い | 暗すぎるとコースが読みにくい。路面と縁石の明るさ管理が必要 | Inertial Drift（ネオン・パステル）、WipEout（デザイン体系の統一） |
| B. おもちゃの学校 | 車を小さく見せ、机・黒板・跳び箱を巨大な地形にする | 一目で分かる独自性 | コースの再設計とモデル制作が大量に要る | Hot Wheels Unleashed、Micro Machines |
| C. 昼のフラットなローポリ | 頂点カラー中心、色数を絞った明るい画面 | 安定して見やすい | 全モデルの作り直しが必要。写実的な車と合わない | Hotshot Racing |

Hotshot Racing は「清潔でカラフル、速く見える」ことを狙い、写実や映画的な照明を追わない方針を取った。ローポリでも 60fps のための最適化は必要だったと開発者が述べている（[GamingBolt](https://gamingbolt.com/hotshot-racing-interview-bringing-back-arcade-racers)）。**「写実を追わず、方向性を一つ決めて全体を揃える」こと自体が、少人数で見栄えを出す定石。**

## 4. グラフィックの改善案（案 A を前提）

規模は Unity 開発者 1 人の概算実働日。

**まず直す不具合（方向性に関係なく必要。合計 1 日程度）**

| ID | 内容 | 直し方の候補 |
|---|---|---|
| V0-1 | スタート直後の木が白い破片の塊に見える | 葉のマテリアルを Alpha Clip（切り抜き）にする。直らなければ、その木を差し替えるか消す |
| V0-2 | 高速時に自車がブラーでにじむ | モーションブラーをカメラの動きだけに効く方式にする（Unity の資料では、この方式の方が負荷も軽い）か、強さを下げる。自車は常にくっきり見せる |
| V0-3 | チューブの壁が黄土色 | チューブ内だけ環境光を暗い青紫にする（Volume または壁のマテリアル）。ネオンが映える |

**方向性を決めてからの改善**

| ID | 内容 | 効果 | 規模 | 参考 |
|---|---|---|---|---|
| V1 | **夜空と遠景**：グラデーションの夜空・月・星のスカイボックス、遠くの山と街明かりのシルエット、高さフォグ。地面を校庭（土・芝）の素材に | 「仮組み感」の最大の原因を消す | 1–2 日 | — |
| V2 | **校舎を夜の顔にする**：窓を発光させる（教室ごとにランダムに点灯）、屋上の縁にネオンライン、文化祭の垂れ幕、提灯の列 | 灰色の箱が「夜の学校」に見える | 2–3 日 | Night-Runners（時代や場所の小物を揃えて舞台の説得力を出す） |
| V3 | **バリアの作り直し**：六角形の膜をやめ、低い壁＋細い発光ライン＋赤白の縁石に | 画面のノイズを減らし、走路を読みやすく | 1–2 日 | WipEout |
| V4 | **車をネオンの世界に馴染ませる**：P1 はシアン、P2 はピンクの車体下の発光、テールランプの光跡（Trail Renderer）、車体の縁の光 | 写実的な車と環境のズレを埋める。どちらが自分か一目で分かる | 1 日 | Inertial Drift |
| V5 | **路面の反射**：濡れたアスファルト（滑らかさを上げ、ネオンを映す反射プローブ）、水たまり | 夜のネオンが路面に映り、画面の情報量が一気に増える | 1–2 日 | HUD の完成イメージの雨の路面 |
| V6 | **流れる光で速度感**：チューブ内に光のリングを等間隔に並べる、路肩の街灯・提灯の列。ブースト時だけ放射状のぼかし（URP の Full Screen Pass Renderer Feature） | 既存の FOV・スピードラインに「景色が流れる」感覚を足す | 1–2 日 | [Unity URP Motion Blur](https://docs.unity.com/en-us/engine/6000.5/manual/post-processing-and-full-screen-effects/urp/effect-list/motion-blur) |
| V7 | **区域ごとの色**：校庭＝提灯の橙、校舎沿い＝桜のピンク、チューブ＝シアン・紫。Volume を区域ごとに切り替え | コースの場所が色で分かり、ミニマップを見なくても位置が伝わる | 1 日 | art of rally（色とシルエットで景観をまとめる） |
| V8 | **縁日の賑わい**：屋台、観客の板ポリ（ビルボード）、打ち上げ花火（最終周・ゴール） | 「展示で遊ぶお祭りのゲーム」という印象 | 2–3 日 | — |
| V9 | **タイトル画面を実際のコースに合わせる**：夜の学校と縁日のネオンを背景にした絵に差し替える（HUD の完成イメージも同様） | 「首都高のゲーム」と思って遊んだら学校だった、という期待外れをなくす | 0.5–1 日 | — |
| V10 | **アイテムボックスに形と光を**：無地の立方体を、回転して光る「？」の箱や提灯の形にする | 何が拾える物か一目で分かる | 0.5 日 | マリオカートのアイテムボックス |

2 画面を 1 台で描くため、V5・V6・V8 は実機の 2 画面でフレーム時間を測ってから採用する（[StandalonePerformance.md](StandalonePerformance.md)）。光の数を増やすより、**発光するマテリアル＋Bloom**で見せるのが負荷の面で有利。

## 5. 面白さの改善案（未実装・未計画のもの）

| ID | 内容 | 効果 | 規模 | 参考 |
|---|---|---|---|---|
| F1 | **ライバルとの差を常に見せる**：「相手まで 0.8 秒」の差表示、後ろから迫ると画面端が光る、並走中は「SIDE BY SIDE」 | 接戦であることが本人に伝わり、スリップストリームを狙う動機になる | 1–2 日 | Mario Kart Arcade GP DX（対面の筐体） |
| F2 | **抜いた瞬間の演出**：「逆転！」の表示、効果音、相手の画面には「抜かれた！」 | 対戦でいちばん盛り上がる瞬間を強調する | 1 日 | — |
| F3 | **実況ボイス**：「並んだ！」「最終周！」「逆転！」「ゴール目前！」を状況に合わせて短く流す（合成音声で可） | HUD を見ない初心者と観客にも展開が伝わる | 2–3 日 | Ridge Racer の実況（[TechRadar](https://www.techradar.com/gaming/arcade-archives-2-ridge-racer-review)） |
| F4 | **最終周の変化**：照明の切り替え、花火、「FINAL LAP」の表示（最終周の曲は既存） | 終盤の盛り上がりを作る | 1–2 日 | OutRun 2 の、場面に合わせた音楽の構成（[Game Developer](https://www.gamedeveloper.com/audio/staying-in-tune-richard-jacques-on-game-music-s-past-present-and-future)） |
| F5 | **僅差のゴールを写真判定で見せる**：差が 1 秒未満なら、ゴールの瞬間をスローで再生 | 決着が劇的になり、観客も結果を共有できる | 2–3 日 | — |
| F6 | **観客向けの画面**：3 台目のモニター（または既存ダッシュボード）に、俯瞰マップと両車の差を大きく表示 | 待っている人が見て楽しめ、次に遊ぶ動機になる | 2–3 日 | 既存の [RaceDashboard.md](RaceDashboard.md) を流用 |
| F7 | **ニアミスの称賛**：相手や CPU の横を僅差で抜けたら「ニアミス！」を表示し、結果画面の称号に数える（加速の報酬は付けない） | ボタンを増やさずに、攻めた走りが認められる | 1–2 日 | Burnout 3（危険な運転がブーストになる） |
| F8 | **ゴール後の数秒のハイライト再生**：決定的な追い抜きや大ジャンプを自動で再生 | 遊んだ人の「見せ場」が残る。ゴースト機能の位置記録と共通化できる | 3–5 日 | — |

**体験の作り方の原則**：演出は短く、何が成功した結果なのかと結び付ける。画面を常に揺らすなど、演出を盛りすぎると逆効果になる（GDC「[Juice it or Lose it](https://www.gdcvault.com/play/1016487/Juice-It-or-Lose-It)」と、反論の講演「[Don't Juice It or Lose It](https://www.gdcvault.com/play/1020861/don-t-juice-it-or)」）。カメラの揺れや一瞬の停止の使い方は「[The Art of Screenshake](https://www.youtube.com/watch?v=AJdEqssNZ-U)」が具体的。

## 6. 推奨する順序

| 段階 | 内容 | 目安 |
|---|---|---|
| 0 | 見た目の不具合 V0-1〜3 を直す。機能ブランチをマージし、人による試遊を 3〜5 組行って現状を記録する | 2 日 |
| 1 | アートの方向性を決め、V1・V2・V3・V4・V9（屋外の夜化、校舎、バリア、車の発光、タイトル） | 6–9 日 |
| 2 | F1・F2・F4（ライバル差、逆転、最終周）と V7（区域の色） | 4–5 日 |
| 3 | V5・V6（路面反射、流れる光）を 2 画面の負荷を測りながら | 2–4 日 |
| 4 | F3・F5・F6（実況、写真判定、観客画面）、V8（縁日の賑わい） | 6–9 日 |
| 5 | F7・F8（ニアミス、ハイライト再生） | 4–7 日 |

段階 1 は見た目の印象を最も大きく変え、段階 2 は対戦の盛り上がりを最も安く上げる。

## 7. 参考資料

- Hotshot Racing 開発者インタビュー：https://gamingbolt.com/hotshot-racing-interview-bringing-back-arcade-racers
- art of rally 開発者インタビュー：https://gamingbolt.com/art-of-rally-interview-art-style-development-and-more
- Hot Wheels Unleashed 開発者インタビュー：https://www.unrealengine.com/developer-interviews/meticulously-detailed-virtual-toy-cars-fuel-racer-hot-wheels-unleashed
- WipEout のビジュアルデザイン：https://www.creativereview.co.uk/an-oral-history-of-wipeout/
- Inertial Drift の評：https://as.com/meristation/2020/09/18/analisis/1600418263_422657.html
- GDC Juice it or Lose it：https://www.gdcvault.com/play/1016487/Juice-It-or-Lose-It
- GDC Don't Juice It or Lose It：https://www.gdcvault.com/play/1020861/don-t-juice-it-or
- The Art of Screenshake：https://www.youtube.com/watch?v=AJdEqssNZ-U
- GDC 2018 Vehicle Feel Masterclass（資料）：https://media.gdcvault.com/gdc2018/presentations/Harris_Matthew_VehicleFeelMasterclass.pdf
- Daytona USA マニュアル（チェックポイントでの時間延長）：https://segaretro.org/images/4/48/Daytonausa_sat_us_manual.pdf
- Mario Kart Arcade GP DX 製品情報：https://bandainamco-am.com/collections/mario-kart-arcade-gp-dx
- Ridge Racer の評（実況）：https://www.techradar.com/gaming/arcade-archives-2-ridge-racer-review
- Richard Jacques インタビュー（OutRun 2 の音楽）：https://www.gamedeveloper.com/audio/staying-in-tune-richard-jacques-on-game-music-s-past-present-and-future
- Unity URP Motion Blur：https://docs.unity.com/en-us/engine/6000.5/manual/post-processing-and-full-screen-effects/urp/effect-list/motion-blur
- Unity URP Screen Space Lens Flare：https://docs.unity.com/en-us/engine/6000.7/manual/visual-effects/lens-flares/lens-flare/post-processing-screen-space
- Unity VFX Graph：https://docs.unity.com/en-us/engine/6000.7/manual/visual-effects/vfxgraph
