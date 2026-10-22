using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Checks BGM loading and the native buffer-control ABI without opening speakers.</summary>
public static class AudioBufferingValidation
{
    [MenuItem("Racing/Validation/Audio Buffering")]
    public static void Run()
    {
        foreach (string track in new[] { "Race_Opening_v2", "finish_v2", "racegame_v3", "racegame_v3_final lap" })
        {
            string streamedPath = $"Assets/Resources/Audio/BGM/{track}.mp3";
            var streamedImporter = AssetImporter.GetAtPath(streamedPath) as AudioImporter;
            Require(streamedImporter != null && streamedImporter.defaultSampleSettings.loadType == AudioClipLoadType.Streaming,
                $"Long BGM {track} must stream instead of allocating its entire decoded PCM at startup.");
            AudioClip streamed = AssetDatabase.LoadAssetAtPath<AudioClip>(streamedPath);
            Require(streamed != null && streamed.samples > 0 && streamed.channels == 2,
                $"Streamed BGM {track} must contain valid stereo audio.");
        }
        const string path = "Assets/Resources/Audio/BGM/TachibanaMoroe.mp3";
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        Require(importer != null && importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad,
            "The existing tutorial BGM must remain decoded before playback.");
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        Require(clip != null && clip.loadState == AudioDataLoadState.Loaded && clip.samples > 0,
            "The preloaded tutorial BGM must contain decoded audio.");
        float[] samples = new float[1024 * clip.channels];
        Require(clip.GetData(samples, 0), "Decoded tutorial PCM must be readable.");
#if UNITY_EDITOR_OSX
        const BindingFlags native = BindingFlags.Static | BindingFlags.NonPublic;
        Type output = typeof(RaceAudioOutput);
        // Invalid player indices deliberately avoid changing any live device.
        output.GetMethod("ra_set_min_buffer_milliseconds", native).Invoke(null, new object[] { -1, 120 });
        ulong count = (ulong)output.GetMethod("ra_get_underruns", native).Invoke(null, new object[] { -1 });
        Require(count == 0, "Native diagnostics must handle absent outputs safely.");
#endif
        Debug.Log($"AUDIO_BUFFERING_VALIDATION_PASS: four long stereo tracks stream; predecoded tutorial {clip.frequency}Hz/{clip.channels}ch; native buffer/diagnostic entry points loaded; no output device opened.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
