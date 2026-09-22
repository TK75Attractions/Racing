
using UnityEngine;

public class CarSoundController : MonoBehaviour
{
    public EngineAudioCore audioCore;
    public Rigidbody carRigidbody;

    [Header("Engine Settings")]
    public float maxSpeed = 30f;
    public float minRpm = 800f;
    public float maxRpm = 7000f;
    public float maxAcceleration = 15f; // 加速度の最大値（負荷の最大判定用）

    private Vector3 lastVelocity;
    private float forwardAcceleration;

    void FixedUpdate()
    {
        if (carRigidbody == null || audioCore == null) return;

        // 1. 全加速度ベクトルを計算
        Vector3 currentVelocity = carRigidbody.linearVelocity;
        Vector3 accelerationVector = (currentVelocity - lastVelocity) / Time.fixedDeltaTime;
        lastVelocity = currentVelocity;

        // 2. 「前方」への加速度のみを抽出（内積を使用）
        // 車両の前方向 (transform.forward) と加速度ベクトルの内積をとる
        forwardAcceleration = Vector3.Dot(accelerationVector, transform.forward);

        // 3. RPM計算（速度依存）
        float rpm = Mathf.Lerp(minRpm, maxRpm, Mathf.Clamp01(currentVelocity.magnitude / maxSpeed));

        // 4. 前方加速度を負荷（0～1）に変換
        // 負の加速（ブレーキやエンジンブレーキ）も考慮するなら、Mathf.Clamp01で0以下をカット
        float load = Mathf.Clamp01(forwardAcceleration / maxAcceleration);

        // デバッグ表示
        // Debug.Log($"Forward Accel: {forwardAcceleration}, Load: {load}");

        audioCore.UpdateParameters(rpm, load);
    }
}