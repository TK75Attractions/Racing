using UnityEngine;

public class EngineAudioCore : MonoBehaviour
{
    
    private double phase;
    private double samplingRate;


    [SerializeField] private float targetFrequency;
    private float targetGain;
    private System.Random sysRandom = new System.Random();

    // わけんの cylinders / phases に相当する設定
    private int cylinders = 4;
    private float[] phases;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        samplingRate = AudioSettings.outputSampleRate;

        // シリンダーごとの位相初期化
        phases = new float[cylinders];
        for (int i = 0; i < cylinders; i++)
        {
            phases[i] = (i * 4f * Mathf.PI / cylinders) + Random.Range(-0.1f, 0.1f);
        }

        AudioSource aud = GetComponent<AudioSource>();
        if (aud != null)
        {
            aud.spatialBlend = 0;
            aud.Play();
        }
    }

    //ほかのスクリプトから音を変える
    public void UpdateParameters(float frequency, float gain)
    {
        this.targetFrequency = frequency;
        this.targetGain = gain;
    }

    // Update is called once per frame
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (samplingRate <= 0) return;

        // targetFrequency を RPM として計算
        float currentRpm = Mathf.Max(100f, targetFrequency);
        float baseFreq = currentRpm / 60f;
        float fireFreq = baseFreq * (cylinders / 2f);

        double phaseIncrement = 1.0 / samplingRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            // --- ここから：元の「のこぎり波」を Python ロジックに差し替え ---

            // 1. シリンダー爆発パルス
            float pulse = 0f;
            for (int c = 0; c < cylinders; c++)
            {
                double tPhase = (phase * baseFreq + phases[c] / (2.0 * Mathf.PI)) % 1.0;
                float fire = (Mathf.Sin((float)(2.0 * Mathf.PI * baseFreq * phase + phases[c])) > 0.95f) ? 1f : 0f;
                float envelope = Mathf.Exp(-50f * (float)(tPhase % (1.0 / (baseFreq / 2.0))));
                pulse += fire * envelope;
            }

            // 2. 高調波（倍音）
            float harmonics = 0f;
            for (int h = 2; h < 8; h++)
            {
                harmonics += (1f / h) * Mathf.Sin((float)(2.0 * Mathf.PI * fireFreq * h * phase));
            }

            // 3. 吸気・排気ノイズ
            float noise = (float)(sysRandom.NextDouble() * 2.0 - 1.0) * 0.2f;

            // 4. 合成と非線形歪み (tanh)
            float sample = (0.6f * pulse) + (0.3f * harmonics) + (0.2f * noise);
            sample = (float)System.Math.Tanh(sample * 3.0);

            // --- ここまで ---

            // チャンネルへの書き込み
            for (int j = 0; j < channels; j++)
            {
                data[i + j] = sample * targetGain;
            }

            // 位相の更新
            phase += phaseIncrement;
        }
    }
}
