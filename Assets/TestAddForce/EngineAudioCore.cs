using UnityEngine;

public class EngineAudioCore : MonoBehaviour
{
    
    private double phase;
    private double samplingRate;


    [SerializeField] private float targetFrequency;
    private float targetGain;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        samplingRate = AudioSettings.outputSampleRate;
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
        float debugFreq = 440f;
        float debugGain = 0.3f;
        double phaseIncrement = (double)targetFrequency / samplingRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            //のこぎり波
            float sample = (float)(phase * 2.0 - 1.0);

            for(int j = 0; j < channels; j++)
            {
                data[i + j] = sample * targetGain;
            }
            phase += phaseIncrement;
            if (phase >= 1.0) phase -= 1.0;
        }
    }
}
