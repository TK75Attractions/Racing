# 校舎の照明と周辺景観

`CampusAtmosphereController` が現在のSampleSceneの `1016こうしゃ` に、レース前の俯瞰でも読み取れる色と周辺景観を適用する。Gmanagerの起動時に生成し、レース中も景観を残す。現在の校舎配置に合わせた座標なので、別コースへ流用する際は植栽・中庭・街並みの位置を再配置する。

## 見た目

- 同じ方向・強さだった2本の方向ライトを、暖かい主光と弱い青色の補助光へ変更。空・水平・地面の環境光を分け、白く飛びやすかった面に色差を付ける。
- 既存の屋根・壁・窓・窓枠・地面Materialを実行時に複製し、スレート色、アイボリー、青灰色、草地の配色を適用する。屋上に設備を加える。FBXや元Material資産は書き換えない。
- 中庭の歩道・植栽・ベンチ、校舎前の緑地、陸上トラックとフィールド、外周の並木、周辺道路と低層の街並みを追加する。街並みは3色・高さの変化で遠景に層を作る。
- 白い旧樹木を非表示にし、109本の軽量な樹木を植え直す。旧樹木の描画頂点数合計5,766,840に対し、新景観全体は69,688頂点・1 MeshRenderer・14 Material。これは描画対象メッシュの比較であり、FPSの測定結果ではない。景観生成は専用の `System.Random` を使い、ゲーム用の乱数列を進めない。
- 既存の晴れ・曇り・雨／昼・夜を引き継ぎ、夜は新しい窓Materialを暖色で発光させる。

## 影と衝突

通常の影の描画距離50mでは、高さ約260mからの校舎紹介で影が届かない。俯瞰の再生中だけ使用中のURP Assetを距離650m以上／主光シャドウマップ4096以上にし、元の値を保存する。紹介終了・機能OFF・コンポーネント無効化・破棄で復元する。PC設定の検証では50m／2048へ戻った。設定資産の保存は行わない。

新しい装飾にColliderは付けない。旧樹木を隠すときは、その樹木と同じGameObjectのColliderも無効にし、見えない枝の当たり判定を残さない。OFFで旧状態へ戻す。道路・校舎のColliderやMeshColliderの参照は変更しない。背景の地面は元の敷地の外側だけに配置し、地下コースの開口部を塞がない。

## 比較と検証

F8の **Campus lighting / landscaping / town** が既定ON。再生中もOFF／ONでき、複製Material・旧樹木・追加景観・照明を一緒に切り替える。紹介用の背景PlaneのMaterialも追従する。

通常再生・映像の収録:

```sh
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=human-fast-control-7102 \
RACING_BALANCE_TIME_SCALE=1 RACING_BALANCE_VIDEO=1 RACING_BALANCE_WINDOW_SECONDS=3 \
RACING_COURSE_INTRO_CHECK=1 RACING_CAMPUS_ATMOSPHERE_CHECK=1 \
RACING_BALANCE_OUTPUT=Logs/CampusAtmosphere/FinalVerified bash Tools/RunBalancePlaytest.sh
```

切り替え・雨夜・無効化／再有効化の検証:

```sh
RACING_BALANCE_SKILL_SUITE=1 RACING_BALANCE_RUN=human-fast-control-7102 \
RACING_BALANCE_TIME_SCALE=1 RACING_BALANCE_WINDOW_SECONDS=1 \
RACING_COURSE_INTRO_CHECK=1 RACING_CAMPUS_ATMOSPHERE_CHECK=1 \
RACING_CAMPUS_ATMOSPHERE_MODE=checks \
RACING_BALANCE_OUTPUT=Logs/CampusAtmosphere/LiveChecks bash Tools/RunBalancePlaytest.sh
```

`CampusAtmospherePlaytestProbe` が生成メッシュ量、装飾のColliderなし、切り替えと夜の発光、元の影設定への復元、校舎・道路Colliderの保持を確認する。地形共存用のMeshが通常のStart処理で整うため、Collider比較の基準は紹介開始時の準備済みグリッドに置く。`CourseIntroPlaytestProbe` も併用し、両画面同期・紹介中の時計停止・車両保持・3秒カウントダウンを確認した。

[動画・比較画像と確認結果](../Artifacts/CampusAtmosphere/README.md)。既存コアの単体テストは108件成功。今回の走行観測は発進後3秒／1秒であり、完走試験には数えない。
