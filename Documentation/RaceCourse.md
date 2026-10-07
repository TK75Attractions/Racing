# Sceneビューでの3Dコース編集

`RaceCourse` を持つ GameObject を選択すると、Sceneビューに中心線（水色）、左右の境界（黄色）、範囲を示す半透明の帯を表示します。描いた帯はコースの定義・判定用で、道路メッシュやColliderは生成しません。

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
- Shift＋クリックで最寄りの線区間に点を挿入します。点はクリックした表面または描画平面に置かれます。
- Delete / Backspace、または「選択点を削除」で選択した点を削除します。
- 「全ての点」で点の一覧を編集できます。
- クリック、ドラッグ、移動、幅変更、削除はUnityのUndo / Redoに対応します。連続描画は1ストローク単位で取り消せます。

既存の `waypoints.position` は従来どおりローカルX/Zとして保持します。新しい `height` はローカルYです。高さを持たない既存シーン・Prefabは高さ0のまま読み込めるので、移行操作は不要です。編集後は通常どおりシーン・Prefabを保存してください。

## 逸脱判定

`IsPointInsideCourse(Vector3)` はSceneに表示した帯の三角形と、その場所の路面高さを使います。`Vertical Tolerance` は路面から上下に許容するワールド距離で、車体の原点の高さやジャンプ量に合わせて設定します。立体交差の上下間隔より十分小さくすると、上下の道路の間に落ちた車を範囲外として判定できます。

`LapManager` は車の3D位置を使って `isOffCourse` と `offCourseTimer` を更新します。`Respawn When Off Course` が有効なら、従来の遅延設定に従ってリスポーンします。無効でも逸脱の状態は取得できます。点が不足している場合や全長0のコースでは判定を行いません。

幅0の区間や、XZへの投影面積が0になる垂直な区間は走行範囲を持ちません。この仕組みは高さのある道路・坂道用で、垂直な壁面走行やバンク角の定義には対応していません。

`IsPointInsideCourse(Vector2)` は互換用の高さを無視するAPIです。車の逸脱検知にはVector3版を使用します。3Dの最寄り点、進捗距離、進行方向の取得も高さを考慮するため、立体交差で上段・下段の区間を区別できます。

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

`Racing > Validate Race Course` で、旧データの読込、閉路の継ぎ目、坂道の距離・高さ・幅、見える境界と判定の一致、立体交差、装飾サンプル、Transform変更、Undo、空コース・重複点、Scene編集による点の追加・挿入・削除とストローク単位のUndo / Redo、`LapManager` の逸脱検知と復帰を検証します。

バッチ実行: `-batchmode -nographics -executeMethod RaceCourseValidation.Run -quit`
