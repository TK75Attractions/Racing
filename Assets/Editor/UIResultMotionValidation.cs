#if UNITY_EDITOR
using TMPro;
using UnityEngine;

public static class UIResultMotionValidation
{
    public static void Run()
    {
        UIRaceEventMotionValidation.Run();
        for(int player=0;player<2;player++)
        {
            var root=new GameObject("Result motion QA",typeof(RectTransform));
            try
            {
                var ui=new ResultUIManager();ui.Init(root.transform,player+1);
                var record=new RaceResultRecord{playerNumber=player+1,finishPosition=1,didFinish=true,totalRaceTime=92.461f};ui.ShowResults(record);
                var card=root.transform.Find("ResultPresentation/ResultCard");var motion=card.GetComponent<UIRevealSequence>();
                motion.Play();motion.Tick(.08f);
                UIMotionValidation.Require(card.Find("Header").GetComponent<CanvasGroup>().alpha>card.Find("Finish/Time").GetComponent<CanvasGroup>().alpha,"Results introduce the headline before the finish time.");
                motion.Tick(.20f);
                UIMotionValidation.Require(card.Find("Classification/ResultRow1").GetComponent<CanvasGroup>().alpha>card.Find("Classification/ResultRow4").GetComponent<CanvasGroup>().alpha,"The current classification hierarchy must reveal in order.");
                UIMotionValidation.Require(card.Find("Finish/Time").GetComponent<TMP_Text>().text=="1:32.461","Motion must never replace the actual finish time with a counting value.");
                motion.Tick(1);motion.SampleExit(.5f);motion.Cancel();
                foreach(string path in new[]{"Header","Finish","Finish/Rank","Finish/Time","Classification/ResultRow4","DailyRanking","ResultMenu"})
                {
                    var part=card.Find(path) as RectTransform;
                    UIMotionValidation.Require(part.anchoredPosition==Vector2.zero&&part.localScale==Vector3.one&&part.GetComponent<CanvasGroup>().alpha==1,"Cancelled results restore every panel: "+path);
                }
                record.didFinish=false;ui.ShowResults(record);motion.Play();motion.Tick(1);
                UIMotionValidation.Require(card.Find("Finish/Rank").GetComponent<TMP_Text>().text=="DNF"&&card.Find("Finish/Time").GetComponent<TMP_Text>().text=="未完走","DNF stays factual during staged playback.");
            }
            finally{Object.DestroyImmediate(root);}
        }
        Debug.Log("UI_RESULT_MOTION_PASS: actual row bindings, staged headline/rank/time/classification, factual times/DNF, replay and exit interruption for both palettes.");
    }
}
#endif
