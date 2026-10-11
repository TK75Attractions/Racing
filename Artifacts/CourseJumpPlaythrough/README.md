# スタート・坂・ジャンプの実車試走

2026-10-10、Unity 6000.3.9f1。実シーン・既存FBX・実際の car2 Prefab を持つ検証用コピーの Play Mode で、P1・P2をペダルとハンドルの入力だけで一周させました。試走中に車両の位置・速度を補正していません。

- 両車とも約1.64kmのコースを完走。
- 壁への接触0件、停止・転倒なし。
- 踏切前の直線から約12mの空中区間を離陸し、両車とも着地。全輪が浮いていた時間は約0.66 / 0.68秒。
- 道路三角形の反転0件。
- 一周後の別テストでP2をコース外に置き、4秒経過しても位置が戻されないことを確認。
- 道路生成・ジャンプ区間・長距離の補間精度・コース編集・LapManagerの回帰検証も成功。

report.json は全物理フレームから集計した結果です。trace.csv はログの10フレームおき（約0.2秒間隔）の抜粋、driving-trace.svg は実走の平面軌跡と高さです。試走は中程度の速度を使った入力制御による確認で、任意の運転入力を保証する試験ではありません。

再実行: Racing > Validate Start And Jump Route (Play Mode)。バッチは -batchmode -nographics -executeMethod CourseJumpPlaythroughValidation.RunBatch（-quitなし）。RACING_COURSE_PLAYTHROUGH_OUTPUT で出力先を指定できます。
