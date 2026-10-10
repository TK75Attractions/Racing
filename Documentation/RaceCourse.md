# Sceneビューでの3Dコース編集

`RaceCourse` を持つ GameObject を選択すると、Sceneビューに中心線（水色）、左右の境界（黄色）、範囲を示す半透明の帯を表示します。中心線・高さ・幅から、アスファルトの道路メッシュ、中央の白い破線、両端の赤白模様、路面のMeshColliderも自動生成します。既存の `RaceCourse` にも適用されるので、別のコンポーネントを追加する必要はありません。

## 描画する

1. 既存の `RaceCourse` を選択するか、`GameObject > Racing > Race Course` で作成します。
2. Inspectorの「描画を開始 / 末尾に追記」を押します。
3. Sceneで左クリックすると点を追加します。左ドラッグでは「ドラッグの点間隔」以上離れるたびに点を追加して線を描きます。
4. 「描画を終了」またはEscで編集モードに戻ります。

「Colliderの表面に配置」を有効にすると、クリック位置のColliderに沿って高さも取得します。トリガーは対象外です。Colliderがない場合やこの設定を無効にした場合は、「描画平面の高さ (World Y)」に描きます。Alt＋マウス操作はSceneの視点操作に使えます。

「Closed Loop」が有効なら末尾から先頭へ線をつないで周回コースにします。途中まで描く際は無効にしておくと確認しやすくなります。点の配列順が進行方向で、先頭が距離0の開始点です。既存の周回・チェックポイントの設定は `LapManager` で引き続き行います。

## 形状と幅を調整する

- 点をクリックして選択し、XYZハンドルで移動します。上下のハンドルで坂道や立体交差の高さを調整できます。
- 緑のハンドルで選択点のコース幅を変更します。幅は左右合わせたワールド単位の値です。GameObjectのScaleを変更しても幅は同じです。
- Inspectorの「位置 (Local XYZ)」「幅 (World)」でも正確に指定できます。
- 「次の点への曲がり」で次の点までの区間を左右に曲げます。0は直線です。高さも区間に沿って補間します。
- 「走行線形」の `Corner Rounding Distance` は角を丸める前後の距離、`Slope Blend Distance` は勾配が変わる場所をつなぐ前後の距離です。初期値は各20m。元の中心線を距離に沿って平均化するので、元の高さの範囲を超える山や谷を作りません。丸める場所では実際の道路が制御点の少し内側を通ります。
- `Maximum Sample Spacing` は道路の分割間隔です。初期値1.5m。長い区間も距離で分割し、坂の頂上や角に大きなポリゴンを作りません。開路の始点・終点は保持し、閉路の継ぎ目も同じ線形でつなぎます。
- 丸め距離を両方0にすると従来の線形・高さ補間になります。丸めた線でも途中への挿入と幅ハンドルは元の制御点の区間に対応します。
- 「次の点までジャンプ区間 (路面なし)」を有効にすると、その区間の道路・模様・Colliderを生成しません。踏切と着地端は丸めず正確に保持し、空中区間の中心線は進捗とミニマップのために残します。前後には十分な直線を置き、幅のある道路の内側が折り返さない線形にしてください。ジャンプ区間には近景マーカーも置きません。
- Shift＋クリックで最寄りの線区間に点を挿入します。点はクリックした表面または描画平面に置かれます。
- Delete / Backspace、または「選択点を削除」で選択した点を削除します。
- 「全ての点」で点の一覧を編集できます。
- クリック、ドラッグ、移動、幅変更、削除はUnityのUndo / Redoに対応します。連続描画は1ストローク単位で取り消せます。

既存の `waypoints.position` は従来どおりローカルX/Zとして保持します。新しい `height` はローカルYです。高さを持たない既存シーン・Prefabは高さ0のまま読み込めるので、移行操作は不要です。編集後は通常どおりシーン・Prefabを保存してください。

## 道路の生成と見た目

Inspectorの「道路生成」で `Generate Road` を有効にすると生成します。点の追加・移動・削除、幅・曲がりの変更、Undo / Redo、GameObjectの移動・回転・Scale変更に追従します。「道路を再生成」ボタンでも更新できます。

`Road` の設定で以下を変更できます。長さ・幅はすべてワールド単位です。

- `Center Line Width`：中央の白線の幅。初期値0.2m。
- `Dash Length` / `Dash Gap`：破線1本の長さと空白の長さ。初期値はそれぞれ3m。
- `Curb Width`：両端の赤白模様の幅。初期値0.6m。0で非表示。
- `Curb Stripe Length`：赤または白1区画の長さ。初期値2m。
- `Asphalt Material` / `White Material` / `Red Material`：任意のMaterial。未指定なら同梱のURP Lit Materialを使用します。
- `Generate Collider`：路面の当たり判定。初期値は有効。
- `Collider Material`：路面の摩擦・反発用PhysicsMaterial。

赤白模様は道路幅の内側に置く平らな模様です。狭い区間では中央線と両端の模様の幅を自動で制限します。模様は路面の各三角形に沿って切り出すため、曲がり・坂・幅の変化で路面へ埋まりません。模様自体にはColliderを追加せず、路面と共通の当たり判定で走行します。

閉路では破線の周期を全長に合わせて調整し、赤白模様は区画数を偶数にして周回の継ぎ目でも赤・白が交互に続きます。開路では末端で模様を切り止めます。道路メッシュはSceneに表示する左右の境界と同じ頂点・三角形を使用し、コース幅の範囲判定と一致します。

生成物は子の `Generated Race Course Road` にまとめます。シーン・Prefabにはコースの点と生成設定を保存し、生成メッシュは読み込み時・ゲーム開始時に再構築します。スクリプト再読み込み・再生成時は古いメッシュを解放します。道路のLayerは `RaceCourse` のGameObjectを引き継ぐため、車のGround CheckやPhysicsの衝突Layer設定で使用するLayerを指定してください。

路面は非凸・非トリガーのMeshColliderです。道路側にはRigidbodyを付けず、静止したコースとして使用します。坂道や立体交差の各高さに実際の接触面があります。幅0・点が不足・全長0のコースは路面を生成しません。既に別の道路Colliderがある場合は、そのColliderの配置も合わせて調整してください。

### SampleSceneのチューブコース

校舎モデルは `Assets/Scenes/1009こうしゃチューブ.fbx` に変更しています。地上のスタート・小さな坂・12mのジャンプを経由して東側の入口から地下のチューブへ入り、西側から地上へ戻る約1,829mの周回コースです。幅は地上18〜20m、坂と踏切16m、チューブ内12mです。

チューブの断面中心に沿って道路を配置し、World Yは断面中心から10m下げています。入口付近は約5m、最深部は約-25mです。元モデルの西側には約21m折り返す区間がありますが、走行線だけでその往復を省いて滑らかにつなぎ、チューブの外形・壁・天井の4,305頂点は変更していません。内側からも壁が見える両面Materialと、両側から衝突するMeshColliderを用意しています。

「次の区間の線形に沿う (トンネルなど)」を有効にした区間は、角の丸め距離を最大16mに制限して管の中に収め、勾配を少なくとも前後35mでつなぎます。境界では通常の丸め距離へ徐々に戻し、入口・出口に急な接続を作りません。一般区間は角35m・勾配25m、最大サンプル間隔1.5mです。道路の積分は倍精度で計算しています。

スタート後の右折は校舎の通れる入口を経由し、次の左折から既存の小さな坂に沿って上ります。坂の上端から約70mの直線で踏切へつなぎ、World X約606〜618mを12mのジャンプ区間にしています。踏切の高さは18m、着地側は16mで、その先でチューブ入口へ下ります。進行方向は点の配列順の逆なので、Inspectorでは着地側の点から踏切側への区間をジャンプとして指定しています。

`Racing > Prepare Tube Course Terrain` で地形の共存用メッシュを再生成できます。元FBXを毎回読み出し、主地面はチューブと地表が交差する断面部分だけ切り抜き、面積の約99.77%を残します。入口の `Plane.012`、坂上の `Plane.009` と `Cube.001` は道路の足元だけ切り抜いて肩の部分を残します。入口の40cmの段差や坂上の床への接触を防ぐため、見た目とColliderの両方に同じ切り抜きを適用します。保存先は `Assets/Scenes/TubeCourseData/` です。

新モデルの追加メッシュにもMeshColliderを付けています。道路へ張り出す枝・葉の一体型MeshColliderは無効にし、木の表示は残しています。チェックポイント・ゴール・アイテム・充電クリスタルの高さも道路に合わせています。スタートは路面の2m上に置き、車が埋まらず着地できるようにしています。`RaceSpeedSceneryController` は生成道路の中央線がある場合に古い黄色の破線を重ねず、近景マーカーを道幅から2.5m外側へ配置します。

## 逸脱判定

`IsPointInsideCourse(Vector3)` はSceneに表示した帯の三角形をXZ平面へ投影して判定します。高さの差は判定に使わないため、描画した線から浮いて走行したりジャンプしたりしても、上から見てコース幅の内側ならコース外になりません。コースの下側や立体交差の道路の間も、XZ位置が帯の内側なら範囲内として扱います。横方向に帯から外れた場合は、浮いていてもコース外になります。

`LapManager` は車の3D位置を使って `isOffCourse` と `offCourseTimer` を更新します。`Respawn When Off Course` は初期値とSampleSceneの両方で無効にしています。コースから3秒以上離れても自動では戻しません。逸脱の状態は引き続き取得できます。有効に戻した場合は、遅延設定に従ってリスポーンします。点が不足している場合や全長0のコースでは判定を行いません。

幅0の区間や、XZへの投影面積が0になる垂直な区間は走行範囲を持ちません。この仕組みは高さのある道路・坂道用で、垂直な壁面走行やバンク角の定義には対応していません。

`IsPointInsideCourse(Vector2)` も同じXZの範囲判定を行います。3Dの最寄り点、進捗距離、進行方向の取得は引き続き高さを考慮するため、立体交差で上段・下段の区間を区別できます。

## 装飾配置用のAPI

`TryGetSampleAtProgress(distance, out RaceCourse.CourseSample sample)` は指定距離の位置、進行方向、右方向、法線、幅、左右の縁を返します。位置と縁はワールド座標です。例えば、一定間隔の距離で呼ぶと、道路の両端に装飾を配置できます。

```csharp
for (float distance = 0f; distance < course.TotalLength; distance += 10f)
{
    if (!course.TryGetSampleAtProgress(distance, out RaceCourse.CourseSample sample)) continue;
    Vector3 placement = sample.rightEdge + sample.right * 2f;
    Quaternion rotation = Quaternion.LookRotation(sample.forward, sample.up);
    // placement と rotation を使って装飾を配置する。
}
```

閉路では距離を周回として扱い、開路では先頭・末尾へ制限します。`CopyCenterPathWorld` と `CopyCourseBandWorld` も3Dの線と境界を取得でき、既存のミニマップや沿道ビジュアルは同じデータを利用します。

## 検証

`Racing > Validate Race Course` で、旧データの読込、閉路の継ぎ目、坂道の距離・高さ・幅、見える境界と判定の一致、浮いている車の範囲判定と自動リスポーンの抑止、立体交差、装飾サンプル、Transform変更、Undo、空コース・重複点、Scene編集による点の追加・挿入・削除とストローク単位のUndo / Redo、`LapManager` の横方向の逸脱検知と復帰を検証します。

バッチ実行: `-batchmode -nographics -executeMethod RaceCourseValidation.Run -quit`

`Racing > Validate Race Course Road` では、道路とコース境界の一致、上向きの衝突面、破線の空白、左右の赤白模様、Materialの読込、坂道・曲がりの路面と模様の一致、周回の継ぎ目、Transform変更、幅編集のUndo、古いメッシュの解放、生成・Collider切替、狭い道・空コースを確認します。独立したPhysics SceneでRigidbodyを落下させ、生成道路上で静止することも検証し、既存の `RaceCourseValidation` も実行します。

道路生成のバッチ実行: `-batchmode -nographics -executeMethod RaceCourseRoadValidation.Run -quit`

`Racing > Validate Start And Jump Route (Play Mode)` は、SampleSceneの実際の `car2` PrefabをP1・P2として生成し、ペダルとハンドル入力だけで一周します。車の位置・速度を補正せず、停止・壁への接触・転倒・ジャンプの離陸と着地を記録します。意図したジャンプ以外で0.3秒を超えて全輪が浮くと検証を失敗にします。最後にP2をコース外に4秒置き、自動で戻されないことも確認します。試走中のFirebase配信は停止します。結果は一時フォルダの `racing-course-playthrough/report.json` と `trace.csv` に出力します。

試走のバッチ実行: `-batchmode -nographics -executeMethod CourseJumpPlaythroughValidation.RunBatch`（`-quit` は付けず、検証終了時に終了します）。出力先は環境変数 `RACING_COURSE_PLAYTHROUGH_OUTPUT` で指定できます。旧コースの確認結果は `Artifacts/CourseJumpPlaythrough/`、新チューブコースの結果は `Artifacts/TubeCourse/` に保存しています。

`Racing > Validate Tube Course Clearance` はSampleSceneをプレビューで開き、道路の折り返した面、道路端を含む地形の重なり、地下の管壁の障害、元チューブの保持、主地面の残存面積を確認します。5mの区間同士で水平の方向変化20°未満、絶対勾配0.5未満、勾配の差0.2未満を条件にしています。意図したジャンプの前後8mは勾配・方向の比較から除外します。

チューブ検証のバッチ実行: `-batchmode -nographics -executeMethod TubeCourseValidation.Run -quit`。レポート出力先は環境変数 `RACING_TUBE_VALIDATION_OUTPUT` で指定できます。
