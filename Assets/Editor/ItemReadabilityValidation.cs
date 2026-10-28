#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises the production item event/UI binding, including competing boost notices.</summary>
public static class ItemReadabilityValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [MenuItem("Racing/Items/Validate First Play Readability")]
    public static void Run()
    {
        RaceItemValidation.Run();
        var root = new GameObject("Item notice regression", typeof(RectTransform));
        var car = new GameObject("Item notice car", typeof(Rigidbody), typeof(DebugMover), typeof(CarItemEffects));
        try
        {
            ArcadeUI.Bind(root.transform, 0);
            var effects = car.GetComponent<CarItemEffects>();
            typeof(CarItemEffects).GetMethod("Awake", Private).Invoke(effects, null);
            var hud = PlayerItemHUD.Create(root.transform); hud.Bind(effects);
            var caption = root.transform.Find("ItemHUD/Toast/Caption").GetComponent<TMP_Text>();
            var title = root.transform.Find("ItemHUD/Toast/Title").GetComponent<TMP_Text>();
            var icon = root.transform.Find("ItemHUD/Toast/ItemIcon").GetComponent<RacingIconGraphic>();
            effects.UseItem(RaceItemType.Rocket, null);
            Require(caption.text.Contains("自動操縦"), "Rocket must explain automatic control instead of only ROCKET ON.");
            Call(hud, "ShowBoostToast");
            Require(title.text == "ロケット", "A simultaneous boost must not erase the item explanation.");
            Call(hud, "UpdateToast", 2f);
            Require(root.transform.Find("ItemHUD/Toast").GetComponent<CanvasGroup>().alpha > 0f,
                "A first-time reader needs more than 1.6 seconds for the item explanation.");
            Require(Mathf.Abs(root.transform.Find("ItemHUD/Toast").GetComponent<RectTransform>().anchoredPosition.x) < .01f,
                "The longer explanation must not start its exit using the old 1.6-second boost lifetime.");
            Call(hud, "UpdateStatus");
            Require(root.transform.Find("ItemHUD/StatusPlate/Status").GetComponent<TMP_Text>().text.Contains("自動操縦"),
                "The cached status display must keep the beginner-friendly rocket guidance.");
            effects.UseItem(RaceItemType.Confuse, null);
            Require(icon.symbol == RacingIconGraphic.Icon.Swap, "Confuse acquisition must use the reversed-steering icon.");
            hud.Bind(null);
            Require(!root.transform.Find("ItemHUD").gameObject.activeSelf, "Unbinding must hide stale item feedback.");
            hud.Bind(effects);
            effects.UseItem(RaceItemType.Shield, null);
            Require(title.text == "シールド" && icon.symbol == RacingIconGraphic.Icon.Shield,
                "Rebinding must keep the real item event subscription.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(car); }
        ValidateGuideTiming();
        ArcadeUIValidation.Run();
        Debug.Log("ITEM_READABILITY_PASS: item captions, notice priority, readable duration, icons and rebind; existing item/UI regression.");
    }
    private static void ValidateGuideTiming()
    {
        var root = new GameObject("Item guide timing", typeof(RectTransform));
        try
        {
            var practice = new DrivingTutorialUI(root.transform, 0);
            var lesson = new DrivingTutorialProgress();
            practice.Update(lesson, DriveInputState.Neutral, 0f, true);
            var guide = root.transform.Find("DrivingTutorial/ItemGuide");
            Require(!guide.gameObject.activeSelf, "The item list must not cover the learner's driving view.");
            lesson.Tick(.01f,true,0,0,0,0); lesson.Tick(.7f,true,1,0,4,0); lesson.Tick(.7f,true,1,10,4,35);
            practice.Update(lesson, DriveInputState.Neutral, 4f, true);
            Require(!guide.gameObject.activeSelf, "Approaching the box requires the road and both instruments to stay clear.");
            Require(lesson.CollectItem(true), "Only contact with the practice box should complete the lesson.");
            practice.Update(lesson, DriveInputState.Neutral, 0f, true);
            Require(guide.gameObject.activeSelf, "A stationary learner can use the waiting time to discover every item.");
            foreach (string name in new[] { "Shield", "Rocket", "Oil", "Confuse", "Charge" })
                Require(guide.Find(name+"/Effect").GetComponent<TMP_Text>().text.Length > 0,
                    "Every item, including the charge crystal, needs an explanation.");
            practice.Dispose();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
