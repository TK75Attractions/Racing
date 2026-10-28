#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UI;

public static class UIMotionPerformanceValidation
{
    public static void Run()
    {
        UIResultMotionValidation.Run();var root=new GameObject("Motion mesh allocation QA",typeof(RectTransform),typeof(Canvas));
        try
        {
            ((RectTransform)root.transform).sizeDelta=new Vector2(1920,1080);
            var hud=RacingHUDBuilder.Build(root.transform,0,2);Canvas.ForceUpdateCanvases();
            var dial=hud.Find("Speed/Gauge").GetComponent<ArcadeDialGraphic>();
            for(int i=0;i<100;i++){dial.SetTargetReading(i%2==0?0:240);dial.Tick(1f/60);dial.Rebuild(CanvasUpdate.PreRender);}
            var mesh=dial.canvasRenderer.GetMesh();
            // The renderer owns this mesh. Inspect it without destroying the Graphic's live geometry.
            UIMotionValidation.Require(mesh.vertexCount>1000,"The allocation probe must build the real instrument mesh.");
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<200;i++){dial.SetTargetReading(i%2==0?0:240);dial.Tick(1f/60);dial.Rebuild(CanvasUpdate.PreRender);}
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            UIMotionValidation.Require(allocated==0,"A warmed, moving instrument mesh must allocate zero managed bytes; got "+allocated);
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("UI_MOTION_PERFORMANCE_PASS: real moving instrument mesh rebuilt 200 times with zero managed allocations after warm-up.");
    }
}
#endif
