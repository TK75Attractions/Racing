# チュートリアル・路面・車両・復帰操作（2026-10-12）

練習画面の説明は上寄りの中央に1枚だけ表示します。ペダルを離す準備、加速、操舵、停車、接続確認、完了待機のうち、その時点で必要な説明に切り替わります。

2026-10-20：加速から操舵へ進む際の自動復帰を除去し、位置・向き・速度を維持するよう修正しました。操舵課題の向きの変化は、その段階へ入った実際の車の向きから測ります。練習だけの10m/sへの速度上書きと、停車課題の復帰時に4m/sを与える処理も除去しました。選択車両の駆動・操舵・抵抗・高速旋回は本番と同じ`DebugMover`を通します。路外・転倒・明示的リセット時の復帰と、練習終了時の停止は維持します。

両プレイヤーの段階遷移で位置・向き・速度の維持を検証し、通常の入力ゲートを使った加速→操舵→停車→カウントダウンまでの実走を通過しました。最大速度25.562m/sで旧上限を超えています。[今回の証跡](../Artifacts/TutorialContinuity/README.md)。

P1はSpace、P2は/（スラッシュ、Right Ctrlも使用可）で最寄りのコース路面へ復帰します。ESP32操作時もキーを使えます。ジャンプ区間の空白と道路端の3mを除き、高さを含む距離で復帰先を選びます。コースの向き・傾きに合わせ、実際のタイヤと車体コライダーを路面から0.04m離して配置します。速度、ドリフト／ブーストをクリアし、完了済みのチェックポイント・周回は維持します。中断メニューの「スタートから」は初期位置に戻ります。

車両の重心をローカルY=-0.2へ下げ、接地時の姿勢補正開始を8度、縦横回転の減衰を6に変更しました。2本以上のタイヤが路面から0.18m以内にあり、車がほぼ上向きの時だけ、路面法線方向へ6〜32m/s²の接地補助を加えます。速度に応じて補助を増やし、小さな跳ねを抑えます。路面から離れる速度が2.5m/sを超える時や、路面が検出できないジャンプ中には補助を加えません。空中の速度抵抗・姿勢補助は従来の設定です。

車体・タイヤの摩擦0のPhysicsMaterialは合成をMinimumに変更します。既定の路面とのAverage合成による追加摩擦をなくし、接地補助が駆動速度・ジャンプ距離を削らないようにします。横滑りと駆動力は従来どおりTireForceで計算します。

音声設定のP1/P2出力は機器名を直接選ぶドロップダウンです。「出力なし」と相手が使用していない機器を表示し、機器の再検出・切断に追従します。ポップアップとクリックを受ける背景は所有プレイヤーのDisplayへ表示します。

## アスファルト画像

内蔵の画像生成ツールで作成しました。レースと練習の両路面に同じ素材を使い、2m単位で繰り返します。ミップマップと異方性フィルタを有効にし、UnityのTextureImporterで高さ画像から法線を生成します。路面メッシュのUVをメートル単位にそろえ、法線マップ用の接線も生成します。

- Albedo: `Assets/Resources/RaceCourse/AsphaltAlbedo.png`
- 法線生成元: `Assets/Resources/RaceCourse/AsphaltHeight.png`
- 素材: `Assets/Resources/RaceCourse/M_RoadAsphalt.mat`

生成プロンプト:

```text
Use case: photorealistic-natural
Asset type: seamless square asphalt albedo texture tile for a Unity racing game, representing 2 meters by 2 meters.
Primary request: dry charcoal-gray road asphalt, fine densely packed angular mineral aggregate grains, subtly varied medium gray stones in dark bitumen, convincing newly worn pavement. Orthographic overhead scan fills the entire square edge to edge, perfectly uniform diffuse illumination without directional light, no perspective, seamless repeating edges. Neutral gray color palette, moderate microcontrast, slight uneven grain at several scales. No road markings, no objects, no puddles, no cracks, no large patches, no vignette, no text or watermark. This is a flat material texture map, not a scene.
```

## 再検証

`Racing > Validate Racing Polish` は最寄り路面・ジャンプ空白・坂道クリアランス・周回情報維持・チュートリアルの説明切替・機器一覧を検証します。`DrivingFeelValidation.RunFullBatch` はキーとドロップダウンのPlay Mode確認、実車の操舵・ドリフト・着地・飛行、小さな浮き上がりからの接地、実際の離陸を検証します。`Racing > Tutorial > Validate and Capture` は両プレイヤーが実際に加速・操舵・停車する練習を実行し、画面画像を保存します。

Unity 6000.3.9f1の一時コピーで、最終版の物理41ケース、操作キー・ドロップダウン、道路生成の既存回帰検証がすべて通過しました。両プレイヤーの練習実走と、実コース1周・ジャンプ・着地も通過しています。音声機器の実際の切替再生は実機確認の対象です。

0.1mの浮き上がりと上向き1m/sを与えたテストでは、全タイヤが離れている時間が修正前の0.22秒から、6m/s走行時0.10秒、20/40m/s時0.06秒になりました。上向き5m/sの離陸では、修正前後とも1.22m上昇し、空中時間0.98秒でした。実コース1周の通常区間ではP1の全タイヤ離れが0秒、P2は最長0.02秒でした。ジャンプ区間はP1が0.62秒、P2が0.64秒の飛行後に着地しました。

測定値は `Artifacts/RacingPolish/before.json`、`after.json`、`course.json`、画面例は `tutorial-steer.png` に保存しています。修正前との比較は今回と同じ41ケースです。自動検証用の仮想キーボードでは、Game Viewのないbatchmodeでも入力が届くよう、一時的にフォーカス設定を切り替えてテスト終了後に復元します。
