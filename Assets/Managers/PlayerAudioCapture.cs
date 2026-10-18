using System;
using UnityEngine;

/// <summary>Unityが生成した音声をプレイヤー別機器へ送り、既定出力への二重再生を防ぎます。</summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public sealed class PlayerAudioCapture : MonoBehaviour
{
    private RaceAudioOutput output;
    private Gmanager manager;
    private AudioSource source;
    private Transform emitter;
    private int stream;
    private int bgmPlayer = -1;
    private volatile float p1Left, p1Right, p2Left, p2Right;

    public void ConfigureMusic(RaceAudioOutput audioOutput, int player)
    {
        output = audioOutput;
        bgmPlayer = player;
        stream = 0;
        source = GetComponent<AudioSource>();
    }

    public void ConfigureEngine(RaceAudioOutput audioOutput, Gmanager gameManager, Transform car, int player)
    {
        output = audioOutput;
        manager = gameManager;
        emitter = car;
        stream = player + 1;
        source = GetComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.panStereo = 0f;
        source.dopplerLevel = 0f;
    }

    private void LateUpdate()
    {
        float gain = Mathf.Clamp01(RaceAudioSettings.Master) * (source != null ? source.volume : 1f);
        if (bgmPlayer >= 0)
        {
            p1Left = p1Right = bgmPlayer == 0 ? gain : 0f;
            p2Left = p2Right = bgmPlayer == 1 ? gain : 0f;
            return;
        }
        GetPerspectiveGains(0, gain, out float left, out float right);
        p1Left = left; p1Right = right;
        GetPerspectiveGains(1, gain, out left, out right);
        p2Left = left; p2Right = right;
    }

    private void GetPerspectiveGains(int player, float gain, out float left, out float right)
    {
        left = right = 0f;
        Transform camera = manager != null ? manager.GetPlayerAudioPerspective(player) : null;
        if (camera == null || emitter == null) return;
        Vector3 relative = camera.InverseTransformPoint(emitter.position);
        float distance = relative.magnitude;
        float attenuation = distance <= 8f ? 1f : Mathf.Clamp01(1f - (distance - 8f) / 72f);
        float pan = distance > 0.01f ? Mathf.Clamp(relative.x / distance, -1f, 1f) : 0f;
        left = gain * attenuation * Mathf.Sqrt((1f - pan) * 0.5f);
        right = gain * attenuation * Mathf.Sqrt((1f + pan) * 0.5f);
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        // Appended AFTER EngineAudioCore, so this filter receives synthesized PCM.
        if (!ReferenceEquals(output, null))
        {
            if (bgmPlayer <= 0) output.Submit(0, stream, data, channels, p1Left, p1Right);
            if (bgmPlayer != 0) output.Submit(1, stream, data, channels, p2Left, p2Right);
        }
        Array.Clear(data, 0, data.Length);
    }
}
