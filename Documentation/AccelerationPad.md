# 加速度盤

`AccelerationPad` は、コース上に設置して車両へ一定時間の追加加速を与える地面設置オブジェクトです。

## 使い方

1. Unity メニューの `Racing > Acceleration Pad > Create Acceleration Pad` で盤面を作成します。
2. Inspector の `地面へ配置` を押すか、Scene ビューで位置・回転を調整します。
3. `盤面サイズ`、`Acceleration`、`Boost Duration` を調整します。
4. Scene ビューの青い矢印が加速方向です。車両が矢印方向へ速度変化を受けます。

盤面には自動で Trigger 用の `BoxCollider` が追加・設定されます。車両が踏んだ瞬間に加速時間が設定値へ更新されるため、連続して別の盤面を踏んだ場合も最後に踏んだ盤面の設定が有効です。

## 画面演出

加速度盤の加速中は `DebugMover.BoostVisualIntensity` が有効になり、`Gmanager` からドリフト加速と同じ `VManager.SetDriftBoost` 経路へ強度 1 で渡されます。したがってプレイヤーごとの Bloom、Motion Blur、Lens Distortion など、既存のドリフト加速演出設定をそのまま共有します。

加速は `ForceMode.VelocityChange` で物理フレームごとに適用するため、車体質量に依存しません。リスポーン、入力源の変更、走行不可、コンポーネント無効化時はドリフト加速と同様に解除されます。
