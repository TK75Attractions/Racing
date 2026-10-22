using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class DailyRankingValidation
{
    [MenuItem("Racing/Validation/Today Ranking")]
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        RacingHUDFontBuilder.Build();
        string output = Environment.GetEnvironmentVariable("RACING_DAILY_RANKING_OUTPUT") ?? "Artifacts/DailyRanking";
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "racing-daily-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            VerifyPersistence(temporary);
            Capture(output);
            File.WriteAllText(Path.Combine(output,"validation.txt"),
                "PASS: restart, both seats, per-play history, duplicate callbacks, midnight, sticky test exclusion, lap separation, backup recovery, corrupt-file preservation, save failure; 720p/1080p P1/P2 renders, local rank beyond top five, live OFF/ON, static Japanese glyphs, DNF/auto/error states.\n");
            Debug.Log("DAILY_RANKING_VALIDATION_PASS: persistence, calendar rollover, exclusions, backup/error paths and P1/P2 result UI.");
        }
        finally { Directory.Delete(temporary,true); }
    }
    private static RaceResultRecord Finish(float seconds, int player = 1) => new RaceResultRecord {
        playerNumber = player, finishPosition = player, didFinish = true, goalLap = 2, completedLaps = 2,
        totalRaceTime = seconds, bestLapTime = seconds/2, finalLapTime = seconds/2, carName = "SPORT CAR" };

    private static void VerifyPersistence(string temporary)
    {
        DateTime time = new DateTime(2026,10,21,23,59,0);
        var store = new DailyRaceRanking(temporary,()=>time);
        store.BeginRace("A",2,false,false,true);
        var first = Finish(100); store.RecordFinish(first); store.RecordFinish(first);
        store.RecordFinish(Finish(110,2));
        Require(store.Snapshot().rows.Count == 2 && store.Snapshot().FindPlayer(2).entry.playNumber == 1,"Persist both seats once, with one play number.");
        store = new DailyRaceRanking(temporary,()=>time);store.BeginRace("A",2,false,false,true);
        store.RecordFinish(Finish(95));
        Require(store.Snapshot().rows.Count == 3 && store.Snapshot().FindPlayer(1).entry.playNumber == 2,"Restart must retain all attempts.");
        time = time.AddMinutes(2);
        store.RecordFinish(first);
        Require(store.Snapshot().rows.Count == 0,"Old result callbacks cannot re-register after midnight.");
        store.RecordFinish(Finish(105,2));
        Require(store.Snapshot().rows.Count == 1,"New-date finish gets today's board.");
        Require(File.Exists(Path.Combine(temporary,"2026-10-21.json")),"Keep previous date on disk.");
        store.BeginRace("A",2,false,false,true);store.ExcludePlayer(1);store.RecordFinish(Finish(90));
        var auto = Finish(90);auto.achievements = new RaceAchievementSnapshot {usedAutodrive=true};store.RecordFinish(auto);
        var wrongLaps=Finish(90,2);wrongLaps.goalLap=1;store.RecordFinish(wrongLaps);
        Require(store.Snapshot().rows.Count == 1,"Constant-pedal/autodrive/mismatched laps must stay out.");
        string path=Path.Combine(temporary,"2026-10-21.json");
        File.WriteAllText(path,"invalid json");
        var recovered = new DailyRaceRanking(temporary,()=>new DateTime(2026,10,21));recovered.BeginRace("A",2,false,false,true);
        Require(recovered.Snapshot().available && recovered.Snapshot().rows.Count == 2,"Load the last complete backup.");
        File.WriteAllText(path+".bak","invalid backup");
        var broken = new DailyRaceRanking(temporary,()=>new DateTime(2026,10,21));broken.BeginRace("A",2,false,false,true);
        broken.RecordFinish(Finish(80));
        Require(!broken.Snapshot().available && File.ReadAllText(path)=="invalid json","Do not overwrite unreadable history.");
        string blocked=Path.Combine(temporary,"not-a-directory");File.WriteAllText(blocked,"blocked");
        var failed = new DailyRaceRanking(blocked,()=>time);failed.BeginRace("A",2,false,false,true);failed.RecordFinish(Finish(90));
        Require(!failed.Snapshot().saved && failed.Snapshot().rows.Count==1,"Report a write failure while retaining the current attempt in memory.");
        string key=DailyRaceRanking.BoardKey(null,2,1,RacePreset.Standard);
        Require(key==DailyRaceRanking.BoardKey(null,2,1,RacePreset.Standard) && key!=DailyRaceRanking.BoardKey(null,1,1,RacePreset.Standard) &&
            key!=DailyRaceRanking.BoardKey(null,2,-1,RacePreset.Standard) && key!=DailyRaceRanking.BoardKey(null,2,1,RacePreset.Experience),"Separate rule boards deterministically.");
    }
    private static void Capture(string output)
    {
        Gmanager previous=Gmanager.Control;
        var managerRoot=new GameObject("Ranking fixture");managerRoot.SetActive(false);
        var root=new GameObject("Ranking results",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var cameraRoot=new GameObject("Ranking UI camera");
        try
        {
            var race=managerRoot.AddComponent<Gmanager>();Gmanager.Control=race;
            var flags=managerRoot.AddComponent<RaceFeatureController>();flags.Configure(race);
            typeof(Gmanager).GetField("<Features>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(race,flags);
            var book=new DailyRankingBook {day="2026-10-21"};var now=new DateTime(2026,10,21,12,0,0);
            for(int i=0;i<5;i++)book.Record("previous"+i,"A",Finish(100+i*5),now);
            var p1=Finish(108.123f);p1.finishPosition=2;p1.achievements=new RaceAchievementSnapshot {landings=2};
            var p2=Finish(125.321f,2);p2.finishPosition=4;
            book.Record("current","A",p1,now);book.Record("current","A",p2,now);
            var view=DailyRankingView.Build(book,"A","current");view.goalLaps=2;
            var session=new RaceSessionResult(4) {dailyRanking=view};session.SetPlayerResult(0,p1);session.SetPlayerResult(1,p2);
            var cpu=Finish(106,3);cpu.isCpu=true;cpu.finishPosition=1;session.SetPlayerResult(2,cpu);
            var cpu2=Finish(119,4);cpu2.isCpu=true;cpu2.finishPosition=3;session.SetPlayerResult(3,cpu2);
            Camera camera=cameraRoot.AddComponent<Camera>();camera.cullingMask=1<<30;camera.nearClipPlane=.1f;camera.farClipPlane=20;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            ResultUIManager ui=new ResultUIManager();
            for(int player=1;player<=2;player++)
            {
                ui.Init(root.transform,player);ui.ShowResults(session);
                Transform panel=root.transform.Find("ResultPresentation/ResultCard/DailyRanking");
                Require(panel.Find("OwnRank").GetComponent<TMP_Text>().text.Contains(player==1?"3位":"7位"),"Show each player's rank even outside the top five.");
                foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))Require(text.font.HasCharacters(text.text),"Missing baked glyph: "+text.text);
                flags.SetValidationFeature(RaceFeature.DailyRanking,false);ui.RefreshDailyRanking();Require(!panel.gameObject.activeSelf,"Live OFF hides the panel.");
                flags.SetValidationFeature(RaceFeature.DailyRanking,true);ui.RefreshDailyRanking();Require(panel.gameObject.activeSelf,"Live ON restores it.");
                foreach(Transform child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=30;
                foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080)})
                    Render(camera,size,Path.Combine(output,$"result-p{player}-{size.x}.png"));
                p1.didFinish=false;ui.RefreshDailyRanking();if(player==1)Require(panel.Find("OwnRank").GetComponent<TMP_Text>().text.Contains("未完走"),"DNF explanation.");p1.didFinish=true;
                view.saved=false;ui.RefreshDailyRanking();Require(panel.Find("OwnRank").GetComponent<TMP_Text>().text.Contains("保存失敗"),"Save failure must be visible.");view.saved=true;
                var local=player==1?p1:p2;
                var facts=local.achievements;local.achievements=new RaceAchievementSnapshot {usedAutodrive=true};
                ui.RefreshDailyRanking();Require(panel.Find("OwnRank").GetComponent<TMP_Text>().text.Contains("テスト走行"),"Explain excluded auto runs.");local.achievements=facts;
                view.available=false;ui.RefreshDailyRanking();Require(panel.Find("Empty").GetComponent<TMP_Text>().text.Contains("履歴を読み込めません"),"Explain unreadable history.");view.available=true;
                UnityEngine.Object.DestroyImmediate(root.transform.Find("ResultPresentation").gameObject);
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraRoot);UnityEngine.Object.DestroyImmediate(managerRoot);Gmanager.Control=previous;}
    }
    private static void Render(Camera camera,Vector2Int size,string path)
    {
        var target=new RenderTexture(size.x,size.y,24);var old=RenderTexture.active;
        Texture2D image=null;
        try
        {
            camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally {RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
