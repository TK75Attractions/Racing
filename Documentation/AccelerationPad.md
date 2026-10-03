# 加速度盤

`AccelerationPad` は、コース上に設置して車両へ一定時間の追加加速を与える地面設置オブジェクトです。

## 使い方

1. Unity メニューの `Racing > Acceleration Pad > Create Acceleration Pad` で盤面を作成します。
2. Inspector の `地面へ配置` を押すか、Scene ビューで位置・回転を調整します。
3. `盤面サイズ`、`Acceleration`、`Instant Speed Bonus`、`Boost Duration` を調整します。
4. Scene ビューの青い矢印は盤面の向きを示します。ブーストは車両の進行方向に沿ってかかり、ほぼ停止している場合は車体の前方へかかります。

盤面には自動で Trigger 用の `BoxCollider` が追加・設定されます。踏んだ瞬間に速度が上がり、その後も設定時間だけ加速します。車両が踏んだ瞬間に加速時間が設定値へ更新されるため、連続して別の盤面を踏んだ場合も最後に踏んだ盤面の設定が有効です。ブースト中に複数の車体 Collider が同じ盤面へ入っても、瞬間加速は重複しません。

## 画面演出

加速度盤の加速中は `DebugMover.BoostVisualIntensity` が有効になり、`Gmanager` からドリフト加速と同じ `VManager.SetDriftBoost` 経路へ強度 1 で渡されます。したがってプレイヤーごとの Bloom、Motion Blur、Lens Distortion など、既存のドリフト加速演出設定をそのまま共有します。

瞬間加速と継続加速は `ForceMode.VelocityChange` で適用するため、車体質量に依存しません。リスポーン、入力源の変更、走行不可、コンポーネント無効化時はドリフト加速と同様に解除されます。
