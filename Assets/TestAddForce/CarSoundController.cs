using UnityEngine;

public class CarSoundController : MonoBehaviour
{
    [Header("Dependencies")]
    public EngineAudioCore audioCore;
    public Rigidbody carRigidbody;

    [Header("Settings")]
    public float maxSpeed = 30f;
    public float minFreq = 50f;
    public float maxFreq = 2000f;
    [Range(0,1)] public float masterVolume = 0.2f;

    [Header("Debug Info")]
    [SerializeField] private float currentSpeed; //デバッグ用 速度表示
    [SerializeField] private float currentFreq; //デバッグ用、周波数表示

    

    // Update is called once per frame
    void Update()
    {
        if (carRigidbody == null || audioCore == null) return;

        //速度割合
        float speed = carRigidbody.linearVelocity.magnitude;
        float ratio = Mathf.Clamp01(speed / maxSpeed);

        //周波数
        float freq = Mathf.Lerp(minFreq, maxFreq, ratio);

        //デバッグ用のInspectorに表示
        currentSpeed = speed;
        currentFreq = freq;

        audioCore.UpdateParameters(freq,1f);

    }
}
