# 区間別バリア断面のコード変更案（実装しない）

この文書と JSON は設計資料。Assets、シーン、設定は変更していない。数値の基準は `verification.json` の SHA-256。`barrier-sections.json` の progress は **新コース全周の中心線の累積3D距離**で、レース進行と逆向き。制御点間の source distance、片側バリアの弧長、チューブ除外後の run 内距離とは異なる。

## 現行コードで必要になる変更

`RaceBarrierPath.cs` の `ControlPoint` は localPosition / wallHeight / outwardFlare のみ。`SetWorldPath` は渡された座標ごとに既定の ControlPoint を作り直す。`BuildSamples` は minimumWallHeight=6 で高さを丸める。`GetArcRadius` は max(2×height, flare/sin60°) なので、flare を0にしても幅は縮まらない。高さ1.1mへの変更は採用しない。

`RaceCourseBarriers.cs` の Row は center/left/right のみ。Build 内の portal 二分探索、閉路の先頭末尾の結合、Create→SetWorldPath の各段階で全周の progress と断面情報を保持する必要がある。`RaceCourseRoad.Build` は既に distances を受け取るが、現状は RaceCourseBarriers.Build に渡していない。したがって、この呼び出しと road Settings の断面設定も最小限の変更対象になる。

概念上の追加データは以下。実コードは未作成。

```csharp
enum BarrierProfileKind { DefaultArc = 0, SlimUpright = 1 }
[Serializable] struct BarrierProfileSection {
    float startProgress, endProgress;
    BarrierProfileKind from, to;
    // Transition length >=10m; this design uses 30m.
}
struct BarrierRow {
    Vector3 center, left, right;
    float rawProgress;
    // Left/right share the road cross-band orientation.
}
struct ProfileSample {
    Vector3 position, outward;
    float rawProgress, slimWeight, thickness;
}
```

1. `RaceCourseRoad.Settings` に区間リストを持たせ、未指定なら全周 DefaultArc。既存 serialized データは enum=0 と空リストで従来動作を保つ。JSON を実行時に直接読む依存は追加しない。
2. `RaceCourseRoad.Build` の distances と設定を RaceCourseBarriers.Build に渡す。全周中心線の progress を Row に持たせ、Row.Lerp で線形補間する。portal での二分探索にも同じ値を使う。closed seam は必要に応じて progress+L に展開してから modulo L で評価し、重複行削除時に位相を失わない。
3. 210/240/1690/1720m の境界および途中0.5m以下の距離に Row を挿入する。元の道路の左右端を同じ補間率で補間する。チューブ/ジャンプの有無に関係なく全周の距離を引き継ぎ、除外 run の始点を0mに戻さない。既存のジャンプ両脇のバリアは残す。本案の断面切替部は踏切・着地そのもの、チューブ内部には置かない。
4. Create→SetWorldPath のオーバーロードに座標・outward・progress・断面設定を渡す。既存シグネチャは DefaultArc を使う互換ラッパーとして残す。manual path は従来の経路接線から outward を計算する。自動生成された course path は道路の左右端から求めた outward を使い、内外カーブのバリア接線による断面方向のずれを防ぐ。
5. ControlPoint / Sample に断面の値を保存する。enum 自体を補間せず、対応する断面の33頂点を同じ順序で補間する。minimumWallHeight の従来6m制限は DefaultArc に維持し、SlimUpright は別の verticalHeight>=6m と半径0.1mを持つ。outwardFlare を細い壁の横幅に読み替えない。

## 採用する細い壁

道路端を (x,h)=(0,0)、x は外向き、h は路面からの高さとする。内面は以下の33点。全区間で上方向に進み、底面で道路につながる。

| 頂点 j | 断面 |
|---|---|
| 0..4 | θ=jπ/8、x=0.1sinθ、h=0.1(1−cosθ)。半径0.1mの足元の丸み |
| 5..16 | x=0.1、h=0.1+5.9(j−4)/12。高さ6mまでの垂直壁 |
| 17..32 | φ=(j−16)π/32、x=0.1cosφ、h=6+0.1sinφ。道路側へ丸める上端 |

内面の最高点6.1m、内面の最大張り出し0.1m。裏面厚0.1m、上端の裏面まで含む高さ6.2m、全金物・Collider の最大外向き張り出し0.2m。現行0.6m厚をそのまま残す設計ではない。0.1m厚の非凸 static MeshCollider を連続した閉メッシュとして生成し、CCD 条件で実測する。厚さだけで安全を宣言しない。

`GetSectionPoint` は断面の33点を取得する関数へ分離。`GetSectionBackNormal` は従来の angle 固定式を断面微分から得る外向き法線へ変更し、Collider の裏面を作る。底面は路面下に0.1m入り、路面上への段差を作らない。上端・端面・裏面を閉じ、法線と三角形の表裏を左右それぞれ確認する。

`BuildSolidFrame` の既存の支柱 .18/.36m とレール .15/.30m は細い壁では外接幅0.2mを超える。Slim 用の背面金物も同じ断面定義から生成し、**全頂点が x<=0.2m、h<=6.2m**を満たすように縮める。単にパネルだけ細くする変更では不十分。金物に独立した旧形状の Collider を残さない。既定断面と青い材質は維持する。

## 切替部

| raw progress | 動作 |
|---|---|
| 0..210 / 1720..L | SlimUpright。閉路のつなぎ目でも同じ形状 |
| 210..240 | 30mかけて Slim→DefaultArc |
| 240..1690 | DefaultArc |
| 1690..1720 | 30mかけて DefaultArc→Slim |

α=3t²−2t³ によって対応する33頂点の座標を補間し、裏面厚も0.1..0.6mで補間。端の一次微分が0になる。中間断面の最高点は両端最高点の補間になり6.1m以上。頂点をただ追加するだけでなく、UV は実断面の隣接点の距離の累積から再計算する。`GetSectionArcDistance` の円弧専用式をそのまま使わない。

切替30mに対する最大外向き幅の変化の上限は、(11.01−0.1)×1.5/30=0.546m/m。道路幅の変化率0.2m/mとは別の量であり、その合格条件を流用しない。壁に沿って高速で走ると切替が横方向の押し返しになるので、30mでも実走確認は必要。

内面と裏面を各行で生成してから隣接行をつなぐ。切替境界で run を分割して隙間を作らない。稠密化した三角形に折り返し・ゼロ面積・隙間がないこと、厚みの法線が反転しないことを将来の実装時に検証する。頂点補間であることだけでは縦方向のメッシュ品質を保証しない。

## 壁走行・検証への影響

`RaceBarrierPath.Rebuild` は現状、断面全頂点の最大張り出しから drivingInner / drivingOuter を作る。`IsPointInsideDrivingFootprint` はこれらのXZ四角形を用い、高さを見ずに走行域を拡張する。新断面でも**実際の補間済み内面の最大幅**で同じキャッシュを再生成する。細い区間を旧円弧の約11mの走行域に残すと、壁外の空間でも復帰判定されなくなる。

細い区間は壁走行の横幅が0.1mに縮み、円弧を登って大きく横へ逃げる遊びがなくなる。走路幅18m以上を保つ代わりに、接触は垂直壁への衝突に近づく。`RaceCourse.IsPointInsideDrivingArea` と LapManager の壁上 off-course タイマー連携を維持する。非生成・disabled・visual-only・別 scene の壁を走行域に入れない現行条件も維持する。

読み取った現行検証と、必要な追加条件は以下。**今回は Unity 検証を実行していない。**

| 既存の検証 | 現行の条件 | 新断面での扱い |
|---|---|---|
| CourseBarrierValidation.Run | 0.5mごと、路面+1mから左右へ、幅/2+16m以内の ray hit、両側・tube 除外・道路端0.01m整合、rebuild後の mesh 解放 | 判定を維持し、断面切替境界・seam・portal 直前直後も対象に追加 |
| CourseBarrierValidation.ValidateContainment | 左右、中心高1m/4m、Box(2,1,4)m、横速度25m/s＋前方10m/s、0.02s×300、abs(X)<24・Y>−1 | 既定の4試行を維持し、Slim の4試行、両方向の切替部、入口端付近でも追加。切替形状の値を fixture に反映し、狭い壁の実外縁を越えない追加判定も行う |
| RaceCourseValidation.ValidateBarrierDrivingArea | 左右×33断面点が走行域内、タイマー解除・respawnなし、最大張り出し+0.2mは外、open end/rebuild/enable/visual-only | DefaultArc の66点を残し、Slim と中間断面の全点を追加。固定の x=±12,y=5 の壁上例は DefaultArc 専用に残し、Slim は実断面から点を採る |
| RaceCourseValidation.ValidateWallStuckRecovery | 実際のバリア Collider と車の停止・復帰挙動 | Slim の垂直面と切替部も対象にして、押し返し・張り付き・復帰位置を確認 |
| RaceBarrierPathValidation | 円弧60°、登れる最初の勾配、内側へ返る上部、既定厚0.6m、ray の既定の衝突位置、32分割など | 既定断面の回帰条件は変更しない。Slim に円弧の数値を適用せず、高さ6m・幅0.1m・厚さ0.1m・同一内面・closed shell・上端6.2m以下の別条件を追加 |

6mの垂直壁は高さ4m・車高1mの既存試行で車の上端4.5mを上回るが、衝突時の回転・跳ね上がりや極端な速度での乗り越え防止はこの高さ比較だけでは証明できない。現行の円弧には約12.6mのかえしがあり、Slim は同等の壁走行や跳躍抑制を保証しない。

## 今回の数値確認と適用保留

`design.py --export` は全校舎FBXと既存の実床切抜き、木の高さ別外形で A/B/C を計算する。Slim B は0.1m幅の外接帯、Blend B は各検証区間の最小 Slim 重み a について `11.01(1−a)+0.1a` 幅の外接帯を使い、途中で大きくなる形状も含める。頂点だけ、切替両端だけの確認ではない。

ただしこれは配置の設計検証であり、上記の Unity メッシュ・物理試験の代用ではない。校舎外の行灯・ゲート等と移動棟との0.3m以上の隙間も入力の全形状がないため保留する。設定リストだけを先行投入したり、既存モデルの切り抜きを追加して合格にする運用は採らない。
