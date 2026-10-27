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
        ArcadeUIValidation.Run();
        Debug.Log("ITEM_READABILITY_PASS: item captions, notice priority, readable duration, icons and rebind; existing item/UI regression.");
    }
    private static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
