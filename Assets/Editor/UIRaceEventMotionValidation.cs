#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;

public static class UIRaceEventMotionValidation
{
    public static void Run()
    {
        UITransitionMotionValidation.Run();var root=new GameObject("Race event motion QA",typeof(RectTransform));
        try
        {
            var hud=RacingHUDBuilder.Build(root.transform,0,2);var lap=new UILap();lap.Init(hud.Find("Lap"));lap.SetLap(2);
            UIMotionValidation.Require(hud.Find("Lap/Ticks").GetComponent<UIRevealSequence>()!=null,"Lap segments must have a finite reveal.");
            var rail=ArcadeNoticeRail.Get(root.transform);rail.Set(0,"ドリフト加速！");var notice=root.transform.Find("QuietRaceNotice");
            UIMotionValidation.Require(notice.GetComponent<UIRevealSequence>()!=null&&((RectTransform)notice).anchorMin.x>=.9f,"Guidance motion stays peripheral.");
            var item=PlayerItemHUD.Create(root.transform);root.transform.Find("ItemHUD").gameObject.SetActive(true);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(PlayerItemHUD).GetMethod("ShowToast",flags).Invoke(item,new object[]{"ブースト発動！","BOOST ON",Color.white});
            typeof(PlayerItemHUD).GetMethod("UpdateToast",flags).Invoke(item,new object[]{.1f});
            var toast=root.transform.Find("ItemHUD/Toast") as RectTransform;
            UIMotionValidation.Require(toast.anchoredPosition.x>0&&toast.localScale==Vector3.one&&toast.anchorMin.x>=.7f,"Boost moves briefly from the edge without zooming into the road.");
            typeof(PlayerItemHUD).GetMethod("UpdateToast",flags).Invoke(item,new object[]{2f});
            UIMotionValidation.Require(toast.GetComponent<CanvasGroup>().alpha==0,"A completed notice must disappear.");
            var goal=GoalCelebrationUI.Create(root.transform,RacingUIFontCatalog.GetHUD(true));var hero=goal.transform.Find("Hero");var motion=hero.GetComponent<UIRevealSequence>();
            motion.Play();motion.Tick(.1f);
            UIMotionValidation.Require(hero.Find("GoalText").GetComponent<CanvasGroup>().alpha>hero.Find("FinishTime").GetComponent<CanvasGroup>().alpha,"Goal headline precedes the factual finish time.");
            motion.Tick(1);goal.HideImmediate();UIMotionValidation.Require(hero.localScale==Vector3.one&&((RectTransform)hero).anchoredPosition==Vector2.zero,"Goal cancellation must reset its pose.");
        }
        finally{Object.DestroyImmediate(root);}
        Debug.Log("UI_RACE_EVENT_MOTION_PASS: peripheral guidance, lap reveal, no-zoom toast travel, finite notices and staged goal typography.");
    }
}
#endif
