using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Preserve native HUD pixels. Camera-space UI shares URP's whole-stack render scale.</summary>
public sealed class RaceRenderBudget : MonoBehaviour
{
    private UniversalRenderPipelineAsset pipeline;
    private float originalScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if(Application.isEditor||FindFirstObjectByType<Gmanager>()==null)return;
        var owner=new GameObject("Race native resolution");DontDestroyOnLoad(owner);owner.AddComponent<RaceRenderBudget>();
    }
    private void Awake()
    {
        pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if(pipeline==null){enabled=false;return;}
        originalScale=pipeline.renderScale;
    }
    private void OnEnable(){if(pipeline!=null)pipeline.renderScale=1f;}
    private void OnDestroy(){if(pipeline!=null)pipeline.renderScale=originalScale;}
}
