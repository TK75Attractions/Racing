using UnityEngine;

public class CarSoundController : MonoBehaviour
{
    [Header("Dependencies")]
    public EngineAudioCore audioCore;
    public Rigidbody carRigidbody;

    [Header("Engine Settings")]
    public float minRpm = 800f;   // アイドリング時の回転数
    public float maxRpm = 7000f;  // レッドゾーンの回転数
    public float maxSpeed = 30f;  // エンジンが最高回転に達する速度

    [Header("Debug Info")]
    [SerializeField] private float currentSpeed;
    [SerializeField] private float currentRpm;

    // Update is called once per frame
    void Update()
    {
        if (carRigidbody == null || audioCore == null) return;

        // 1. 速度からエンジン負荷（0.0 - 1.0）を計算
        float speed = carRigidbody.linearVelocity.magnitude;
        float ratio = Mathf.Clamp01(speed / maxSpeed);

        // 2. 速度をベースにRPMを算出（アイドリング回転数からスタート）
        // 速度が0のときはminRpm、maxSpeedのときはmaxRpmになる
        float rpm = Mathf.Lerp(minRpm, maxRpm, ratio);

        // 3. デバッグ表示
        currentSpeed = speed;
        currentRpm = rpm;

        // 4. EngineAudioCore へ渡す（周波数としてRPMをそのまま使用）
        // gain(音量)も適宜コントロール可能にしておくと良いでしょう
        audioCore.UpdateParameters(rpm, 1f);

    }
}
