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

        // ピッチの計算（ベースの回転数を2000rpmとして調整）
        float pitch = Mathf.Clamp(rpm / 4000f, 0.8f, 1.2f);
        foreach (var s in sources) if (s != null) s.pitch = pitch;

        UpdateVolumes();
    }

    private void UpdateVolumes()
    {
        if (sources == null) return;

        // 【修正2】重なりを増やす（クロスフェードを広くする）
        // 0.0〜1.0 に正規化した RPM を使うと計算が楽になります
        float rpm01 = Mathf.Clamp01(targetRpm / 7000f);

        // 各レイヤーの音量カーブを少し重ねる
        sources[0].volume = Mathf.Clamp01(1f - rpm01 * 3f); // Idle
        
        float lowWeight = Mathf.Clamp01(1f - Mathf.Abs(rpm01 - 0.3f) * 4f);
        sources[1].volume = lowWeight * targetLoad;
        sources[2].volume = lowWeight * (1f - targetLoad);

        float medWeight = Mathf.Clamp01(1f - Mathf.Abs(rpm01 - 0.6f) * 4f);
        sources[3].volume = medWeight * targetLoad;
        sources[4].volume = medWeight * (1f - targetLoad);

        float highWeight = Mathf.Clamp01((rpm01 - 0.5f) * 3f);
        sources[5].volume = highWeight * targetLoad;
        sources[6].volume = highWeight * (1f - targetLoad);
        // 【追加】高速度での音量減衰処理
        // rpm01 が 0.5 (約3500rpm) から徐々に音量が下がり始め、
        // 1.0 (7000rpm) で 0.5倍になるように計算します。
        float speedVolumeMultiplier = Mathf.Lerp(1.0f, 0.5f, Mathf.InverseLerp(0.5f, 1.0f, rpm01));

        foreach (var s in sources)
        {
            s.volume *= speedVolumeMultiplier;
        }
    }
}