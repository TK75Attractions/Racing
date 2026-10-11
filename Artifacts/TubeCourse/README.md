# チューブコースの検証（2026-10-10）

Unity 6000.3.9f1、SampleScene、実際の car2 Prefab（P1・P2）で確認。
コースモデル: `Assets/Scenes/1009こうしゃチューブ.fbx`（提供FBXを変更せず収録）。

- 全長: 1,829.06m。チューブ幅12m、地上幅18〜20m、踏切幅16m。
- チューブの4,305頂点をそのまま保持。内面も描画・衝突可能。
- 主地面の面積99.77%を保持。入口・坂上の床は道路の足元だけ切り抜き。
- 道路の反転した三角形、地形との重なり、管壁の走行障害: いずれも0。
- 5m区間の最大方向変化19.60°。最大勾配0.446（約24.05°）。5m区間の最大勾配差0.084。意図したジャンプ付近はこの比較から除外。
- P1・P2とも1周と12mのジャンプの離陸・着地に成功。停止・壁接触・転倒・走行中の位置補正なし。
- 最大車体傾斜: 両車24.10°未満。意図したジャンプ以外の全輪非接地: P1 0秒、P2 最大0.02秒。
- P2をコース外に4秒置いても自動復帰しないことを確認。

走行はペダルとハンドル入力のみ。直線の目標速度25m/s、曲線14m/s（実際の定常速度は約20m/s・12m/s）、左右約2mのレーンで実施。最高速度や全ての走行位置での保証ではない。

`playthrough.json` が実車の結果、`trace.csv` が50Hzの走行記録、`geometry.json` が線形・地形の検証結果。PNGは実際のシーン設定を使ったUnity/URP描画で、内側・深部のカーブ・両入口・地上全景を目視確認。

再実行:

```text
-executeMethod RaceCourseRoadValidation.Run -batchmode -nographics -quit
-executeMethod TubeCourseValidation.Run -batchmode -nographics -quit
-executeMethod CourseJumpPlaythroughValidation.RunBatch -batchmode -nographics
```

試走には `-quit` を付けない。出力先は `RACING_COURSE_PLAYTHROUGH_OUTPUT` と `RACING_TUBE_VALIDATION_OUTPUT` で指定可能。
