using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ResultHighlightValidation
{
    [MenuItem("Racing/Validation/Result Highlights")]
    public static void Run()
    {
        RacingHUDFontBuilder.Build();
        var root = new GameObject("Result highlight validation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var managerRoot = new GameObject("Result flags"); managerRoot.SetActive(false);
        Gmanager previous = Gmanager.Control;
        RenderTexture target = null; GameObject cameraObject = null;
        try
        {
            Gmanager race = managerRoot.AddComponent<Gmanager>(); race.course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>(); Gmanager.Control = race;
            RaceFeatureController flags = managerRoot.AddComponent<RaceFeatureController>(); flags.Configure(race);
            typeof(Gmanager).GetField("<Features>k__BackingField", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(race, flags);
            var session = new RaceSessionResult(4);
            session.SetPlayerResult(0,new RaceResultRecord {playerNumber=1,finishPosition=2,totalRaceTime=203,completedLaps=2,goalLap=2,
                achievements=new RaceAchievementSnapshot {landings=2,driftReleases=1}});
            session.SetPlayerResult(1,new RaceResultRecord {playerNumber=2,finishPosition=1,totalRaceTime=198,completedLaps=2,goalLap=2,
                achievements=new RaceAchievementSnapshot {usedAutodrive=true,landings=2}});
            session.SetPlayerResult(2,new RaceResultRecord {playerNumber=3,isCpu=true,finishPosition=3,didFinish=false,completedLaps=1,goalLap=2});
            var ui = new ResultUIManager(); ui.Init(root.transform,1); ui.ShowResults(session);
            Require(ResultUIManager.FormatHighlight(session.GetPlayerResult(0)).Contains("着地成功 2回"),"Each player must see their actual achievement.");
            Require(ResultUIManager.FormatHighlight(session.GetPlayerResult(1))=="自動運転テストの記録","AUTO cannot be presented as human achievement.");
            var dnf = new RaceResultRecord {didFinish=false,achievements=new RaceAchievementSnapshot {verifiedProgress=320}};
            Require(ResultUIManager.FormatHighlight(dnf).StartsWith("未完走 / 走行進捗"),"DNF must retain its status and factual fallback.");
            TMP_Text highlight=root.transform.Find("ResultPresentation/ResultCard/Achievement").GetComponent<TMP_Text>();
            Require(highlight.font.HasCharacters(highlight.text),"Static font must contain the achievement text.");
            flags.SetFeature(RaceFeature.ResultHighlights,false); ui.RefreshHighlight();
            Require(highlight.text=="","OFF must hide the result highlight immediately.");
            flags.SetFeature(RaceFeature.ResultHighlights,true); ui.RefreshHighlight();
            Require(highlight.text.Contains("着地成功"),"ON restores the saved snapshot.");
            cameraObject=new GameObject("Result camera");
            Camera camera=cameraObject.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.cullingMask=1<<30;camera.nearClipPlane=.1f;camera.farClipPlane=20f;
            Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1f;
            CanvasScaler scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            foreach(Transform child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
            string output=Environment.GetEnvironmentVariable("RACING_HIGHLIGHT_VALIDATION_OUTPUT")??"/private/tmp/racing-result-highlights";
            System.IO.Directory.CreateDirectory(output);
            foreach(Vector2Int size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                target=new RenderTexture(size.x,size.y,24);camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
                RenderTexture old=RenderTexture.active;RenderTexture.active=target;
                var image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"result-"+size.x+".png"),image.EncodeToPNG());
                RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);target=null;
            }
            Debug.Log("RESULT_HIGHLIGHT_VALIDATION_PASS: per-player snapshot, factual fallback, DNF, AUTO label, live OFF/ON, 720p/1080p renders.");
        }
        finally
        {if(target!=null)UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(root);if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(managerRoot);Gmanager.Control=previous;}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
