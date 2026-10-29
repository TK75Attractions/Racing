# ミニマップ

本番走行中のHUD上部、順位表示の右隣にコースマップを表示する。現在のアーケードUIと同じ角丸パネル・影・文字を使い、P1は青、P2はピンクをアクセントにする。練習は別コースなので表示しない。タイトル・結果ではOnPlayと一緒に隠れる。

- 自車を中央に固定し、各画面のカメラの水平な向きに合わせてマップを回す。車自体の向きは矢印で示す。
- 自車は各画面のアクセント色で大きく、相手は明るい矢印、CPU2台はアクセント色の小さなマーカーで表示する。下部に「自分・相手・CPU」の凡例を置く。
- 遠い車はマップ枠の内側に収め、位置の方向を示す。自車は他車より前面に表示する。
- コース形状はRaceCourseの内側・外側の縁を使い、初期化時にUIメッシュを生成する。追加のカメラやRenderTextureは使わない。
- 毎フレームはコースコンテナと4個の車マーカーの移動・回転のみを更新する。

## 配置と調整

1920×1080の基準画面で、外枠はx=380〜680、上端から40px。マップ部分は300×210、見出しと凡例を含めた高さは約326px。順位、順位表、周回、タイム、メーター、ハンドル表示、アイテム通知と重ならない。

`Gmanager > On Play UI Manager > Mini Map`のMap Size、Screen Margin、Padding、Map Rotation Degrees、Marker Size、Track Border Scaleで調整する。Screen Marginは画面左上から外枠までの余白。色は現在のArcadeUIのプレイヤーパレットに従う。

## 構成

- `UIMiniMap`: 枠・凡例・クリップ領域・車マーカーを構築する。
- `MiniMapProjector`: ワールドXZ座標をアスペクト比を保ってマップ座標へ変換する。
- `MiniMapTrackGraphic`: コース帯のメッシュを生成する。
- `MiniMapMarkerGraphic`: 自車・相手・CPUを示す。
- `OnPlayUIManager`: 現在HUDの初期化と本番走行時の表示を管理する。

実行時の階層は`OnPlay/ModernHUD/MiniMap`。P1/P2の車・CPU・視点は既存の`Gmanager.BindMiniMapCars`でそれぞれ割り当てる。

## 検証

`MiniMapValidation.RunBatch`は投影、縦横比、向き、枠内クリップ、自車中心の維持、両プレイヤーの独立性、遠い相手の表示、現在のHUDとの重なりを検証する。

`ArcadeMiniMapCapture.Run`は実シーンを起動し、タイトルと練習では非表示、本番中はP1/P2の両画面に表示、自車中心、各プレイヤーの色、CPU2台の接続、アイテム通知との分離を検証する。実際のフルHD画面を`Artifacts/ArcadeMiniMap`に保存する。

各画面の時間設定は`Documentation/ScreenTimeLimits.md`を参照。
