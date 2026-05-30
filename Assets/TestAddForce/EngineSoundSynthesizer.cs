using UnityEngine;

public class EngineSoundSynthesizer : MonoBehaviour
{
    
    [Header("Car Stats")]
    public Rigidbody carRigidbody;
    public float maxSpeed = 30f;

    [Header("Sound Settings")]
    public float baseFrequency = 50f;
    public float maxFrequency = 2000f;
    [Range(0,1)] public float gain = 0.2f;

    private double phase;
    private double samplingRate;

    //メモよう変数
    private float currentTargetFrequency;

    void Start()
    {
        samplingRate = AudioSettings.outputSampleRate;

        AudioSource aud = GetComponent<AudioSource>();
        if (aud != null)
        {
            aud.playOnAwake = true;
            aud.spatialBlend = 0;
            aud.Play();
        }
    }

    void Update()
    {
        float speed = 0f;
        if (carRigidbody != null)
        {
            speed = carRigidbody.linearVelocity.magnitude;
        }

        float speedRatio = Mathf.Clamp01(speed / maxSpeed);
        currentTargetFrequency = Mathf.Lerp(baseFrequency, maxFrequency, speedRatio);
    }
    // Update is called once per frame
    void OnAudioFilterRead(float[] data, int channels)
    {
        double phaseIncrement = (double)currentTargetFrequency / samplingRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            
            //のこぎり波
            float sample = (float)(phase * 2.0 - 1.0);

            for (int j = 0; j < channels; j++)
            {
                data[i + j] = sample * gain;
            }

            phase += phaseIncrement;
            if (phase >= 1.0) phase -= 1.0;
        }
    }
}
