using UnityEngine;

public class EngineAudioCore : MonoBehaviour
{
    // Inspectorから直接AudioSourceを登録する方式に変更
    [Header("Audio Sources")]
    [SerializeField] private AudioSource idle;
    [SerializeField] private AudioSource low_on, low_off;
    [SerializeField] private AudioSource med_on, med_off;
    [SerializeField] private AudioSource high_on, high_off;

    private AudioSource[] sources;
    private float targetRpm;
    private float targetLoad;

    void Awake()
    {
        // 配列にまとめる（Inspectorで登録したものがここに入る）
        sources = new AudioSource[] { idle, low_on, low_off, med_on, med_off, high_on, high_off };

        foreach (var s in sources)
        {
            if (s != null)
            {
                s.loop = true;
                s.volume = 0f;
                s.Play();
            }
        }
    }

    public void UpdateParameters(float rpm, float load)
    {
        targetRpm = rpm;
        targetLoad = load;

        // ピッチの計算（ベースの回転数を800rpmとして調整）
        float pitch = Mathf.Clamp(rpm / 800f, 0.5f, 2.5f);
        foreach (var s in sources) if (s != null) s.pitch = pitch;

        UpdateVolumes();
    }

    private void UpdateVolumes()
    {
        if (sources == null) return;

        // 1. Idle (0 - 2000rpm)
        sources[0].volume = Mathf.Clamp01(1f - (targetRpm / 1500f));
        
        // 2. Low (1000 - 3000rpm)
        float lowWeight = Mathf.Clamp01(1f - Mathf.Abs(targetRpm - 2000f) / 1000f);
        sources[1].volume = lowWeight * targetLoad;
        sources[2].volume = lowWeight * (1f - targetLoad);

        // 3. Med (2500 - 5000rpm)
        float medWeight = Mathf.Clamp01(1f - Mathf.Abs(targetRpm - 3750f) / 1250f);
        sources[3].volume = medWeight * targetLoad;
        sources[4].volume = medWeight * (1f - targetLoad);

        // 4. High (4500rpm以上)
        float highWeight = Mathf.Clamp01((targetRpm - 4500f) / 2500f);
        sources[5].volume = highWeight * targetLoad;
        sources[6].volume = highWeight * (1f - targetLoad);
    }
}