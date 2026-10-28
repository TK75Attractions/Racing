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
                ValidateSelection(ui,root.transform,player);
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
    private static void ValidateSelection(ResultUIManager ui,Transform root,int player)
    {
        var menu=root.Find("ResultPresentation/ResultCard/ResultMenu");var palette=ArcadeUI.For(player);
        var left=menu.Find("Option1").GetComponent<PedalButtonFeedback>();var right=menu.Find("Option2").GetComponent<PedalButtonFeedback>();
        foreach(var button in new[]{left,right})
        {
            UIMotionValidation.Require(button.GetComponentsInChildren<RacingPanelGraphic>(false).Length==1,"Only the animated face may remain visible; an old face must not cover the selection.");
            var hit=button.GetComponent<UnityEngine.UI.Image>();
            UIMotionValidation.Require(hit.enabled&&hit.raycastTarget&&hit.color.a==0,"Restyling must preserve the transparent pointer hit target.");
        }
        var leftLabel=menu.Find("Option1/Label").GetComponent<TMP_Text>();var rightLabel=menu.Find("Option2/Label").GetComponent<TMP_Text>();
        ui.SetMenuState(0,0);right.SetPointerState(true,false,true);
        for(int i=0;i<120;i++){left.Tick(1f/60);right.Tick(1f/60);}
        UIMotionValidation.Require(left.IsNavigationSelected&&!right.IsNavigationSelected&&leftLabel.color==palette.Paper&&rightLabel.color==palette.Ink,"Exactly the actual pedal choice gets reversed text contrast, even when the other button is hovered.");
        ui.SetMenuState(1,.5f);
        for(int i=0;i<120;i++){left.Tick(1f/60);right.Tick(1f/60);}
        UIMotionValidation.Require(!left.IsNavigationSelected&&right.IsNavigationSelected&&leftLabel.color==palette.Ink&&rightLabel.color==palette.Paper,"Both result actions must exchange the same selected design, including the secondary action.");
        long allocated=System.GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++){left.Tick(1f/60);right.Tick(1f/60);}
        allocated=System.GC.GetAllocatedBytesForCurrentThread()-allocated;
        UIMotionValidation.Require(allocated==0,"A settled result selection must allocate nothing.");
        Debug.Log("UI_RESULT_SELECTION_PASS: exclusive pedal choice, reversed text contrast, secondary-action selection and zero idle allocations / P"+(player+1));
        ui.SetMenuState(0,0);
    }
}
#endif
