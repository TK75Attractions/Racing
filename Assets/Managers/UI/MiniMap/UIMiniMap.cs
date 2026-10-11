using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面左下に出すレース中のミニマップです。
/// コース形状は <see cref="RaceCourse"/> のキャッシュから初期化時に1度だけ組み立て、
/// 毎フレームの処理は車マーカーの移動と回転だけに抑えています。
/// </summary>
[Serializable]
public class UIMiniMap
{
    private const int MarkerCount = 2;

    [SerializeField] private GameObject root;

    [Header("Layout")]
    [SerializeField] private Vector2 mapSize = new Vector2(300f, 210f);
    [SerializeField] private Vector2 screenMargin = new Vector2(40f, 40f);
    [SerializeField, Min(0f)] private float padding = 14f;
    [SerializeField] private float mapRotationDegrees = 0f;
    [SerializeField, Min(1f)] private float markerSize = 20f;

    [Header("Color")]
    [SerializeField] private Color backgroundColor = new Color(0.01f, 0.02f, 0.05f, 0.42f);
    [SerializeField] private Color trackColor = new Color(0.86f, 0.90f, 0.96f, 0.62f);
    [SerializeField] private Color trackBorderColor = new Color(0.02f, 0.03f, 0.07f, 0.72f);
    [SerializeField, Range(1f, 1.6f)] private float trackBorderScale = 1.18f;
    [SerializeField] private Color ownMarkerColor = new Color(0.15f, 1f, 0.85f, 1f);
    [SerializeField] private Color rivalMarkerColor = new Color(1f, 0.32f, 0.22f, 1f);

    private RectTransform rect;
    private RectTransform content;
    private Transform viewTransform;
    private Image background;
    private MiniMapTrackGraphic trackGraphic;
    private MiniMapTrackGraphic borderGraphic;
    private readonly MiniMapMarkerGraphic[] markers = new MiniMapMarkerGraphic[MarkerCount];
    private readonly Transform[] cars = new Transform[MarkerCount];
    private readonly MiniMapProjector projector = new MiniMapProjector();
    private readonly List<Vector3> innerWorld = new List<Vector3>();
    private readonly List<Vector3> outerWorld = new List<Vector3>();
    private readonly List<Vector2> innerLocal = new List<Vector2>();
    private readonly List<Vector2> outerLocal = new List<Vector2>();
    private int ownPlayerIndex;
    private bool initialized;

    /// <summary>コース形状の組み立てに成功したかどうかです。</summary>
    public bool HasCourse => projector.IsValid;

    /// <summary>OnPlay 配下に MiniMap ノードを用意し、コース形状を焼き込みます。</summary>
    public void Init(Transform onPlayRoot, RaceCourse course, int playerIndex)
    {
        ownPlayerIndex = Mathf.Clamp(playerIndex, 0, MarkerCount - 1);
        initialized = false;

        if (onPlayRoot == null)
        {
            Debug.LogWarning("UIMiniMap requires the OnPlay root transform.");
            return;
        }

        Transform existing = onPlayRoot.Find("MiniMap");
        root = existing != null
            ? existing.gameObject
            : CreateChild(onPlayRoot, "MiniMap", typeof(Image));

        rect = root.GetComponent<RectTransform>();
        ApplyLayout();

        background = root.GetComponent<Image>();
        if (background == null) background = root.AddComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;
        background.enabled = false;
        Transform oldSurface = rect.Find("ModernSurface");
        if (oldSurface != null) oldSurface.gameObject.SetActive(false);
        RectTransform frame = RacingHUDStyle.Plate(rect, "MapFrame", new Vector2(0f, -.25f), new Vector2(1f, 1.30f),
            RacingHUDPlateGraphic.PlateShape.Map, RacingHUDStyle.Teal);
        frame.SetAsFirstSibling();
        RacingHUDStyle.Heading(frame, "コースマップ", "COURSE MAP", .08f, .81f, .92f, .97f, 22f);
        RacingHUDStyle.Label(frame, "You", "あなた / YOU", .12f, .03f, .49f, .14f, 15f, RacingHUDStyle.Text);
        RacingHUDStyle.Label(frame, "Rival", "あいて / RIVAL", .59f, .03f, .97f, .14f, 15f, RacingHUDStyle.Text);
        LegendMarker(frame, "YouMarker", .045f, NeonUI.Pink);
        LegendMarker(frame, "RivalMarker", .515f, Color.white);

        RectTransform viewport = (RectTransform)(rect.Find("Viewport") ?? CreateChild(rect, "Viewport", typeof(RectMask2D)).transform);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        content = (RectTransform)(viewport.Find("Content") ?? CreateChild(viewport, "Content").transform);
        content.anchorMin = content.anchorMax = new Vector2(.5f, .5f);
        content.pivot = new Vector2(.5f, .5f);
        content.sizeDelta = mapSize;
        // Move any pre-existing graphics into the clipped, rotating map content.
        foreach (string name in new[] { "Border", "Track", "Marker_P1", "Marker_P2" })
        {
            Transform oldGraphic = rect.Find(name);
            if (oldGraphic != null) oldGraphic.SetParent(content, false);
        }

        borderGraphic = EnsureGraphic<MiniMapTrackGraphic>(content, "Border", trackBorderColor);
        borderGraphic.WidthScale = .22f * trackBorderScale;
        trackGraphic = EnsureGraphic<MiniMapTrackGraphic>(content, "Track", trackColor);
        trackGraphic.WidthScale = .22f;

        for (int index = 0; index < MarkerCount; index++)
        {
            Color markerColor = index == ownPlayerIndex ? NeonUI.Pink : Color.white;
            markers[index] = EnsureGraphic<MiniMapMarkerGraphic>(content, $"Marker_P{index + 1}", markerColor);
            RectTransform markerRect = markers[index].rectTransform;
            markerRect.anchorMin = new Vector2(0.5f, 0.5f);
            markerRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.sizeDelta = new Vector2(markerSize, markerSize);
            markerRect.localScale = Vector3.one;
            markers[index].gameObject.SetActive(false);
        }

        // 自車マーカーは相手と重なっても見えるよう、常に最前面に置きます。
        markers[ownPlayerIndex].rectTransform.SetAsLastSibling();

        BuildCourse(course);
        // コース形状を組めなかったときは、空の枠だけが残らないよう丸ごと隠します。
        root.SetActive(projector.IsValid);
        initialized = true;
    }

    /// <summary>マーカーが追従する車を設定します。null を渡すとそのマーカーは隠れます。</summary>
    public void SetCars(Transform playerOneCar, Transform playerTwoCar)
    {
        cars[0] = playerOneCar;
        cars[1] = playerTwoCar;
        UpdateMarkers();
    }

    public void SetViewTransform(Transform view)
    {
        viewTransform = view;
        UpdateMarkers();
    }

    /// <summary>自車中心で画面の向きに合わせ、コースとマーカーを一緒に移動・回転します。</summary>
    public void UpdateMarkers()
    {
        if (!initialized || !projector.IsValid) return;

        Transform ownCar = cars[ownPlayerIndex];
        Transform heading = viewTransform != null ? viewTransform : ownCar;
        float yaw = heading != null ? Mathf.Atan2(heading.forward.x, heading.forward.z) * Mathf.Rad2Deg : 0f;
        Quaternion rotation = Quaternion.Euler(0f, 0f, yaw + mapRotationDegrees);
        content.localRotation = rotation;
        content.anchoredPosition = ownCar != null
            ? -(Vector2)(rotation * projector.ToLocal(ownCar.position)) : Vector2.zero;

        for (int index = 0; index < MarkerCount; index++)
        {
            MiniMapMarkerGraphic marker = markers[index];
            if (marker == null) continue;

            Transform car = cars[index];
            if (car == null)
            {
                if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                continue;
            }

            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);

            RectTransform markerRect = marker.rectTransform;
            markerRect.anchoredPosition = projector.ToLocal(car.position);
            markerRect.localRotation = Quaternion.Euler(0f, 0f, projector.ToLocalAngle(car.eulerAngles.y));
        }
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }

    private void BuildCourse(RaceCourse course)
    {
        if (course == null)
        {
            Debug.LogWarning("UIMiniMap: RaceCourse is not assigned; the mini map stays empty.");
            return;
        }

        course.CopyCourseBandWorld(innerWorld, outerWorld);
        if (!projector.Build(innerWorld, outerWorld, mapSize, padding, mapRotationDegrees))
        {
            Debug.LogWarning("UIMiniMap: the course path is too small to build a mini map.");
            borderGraphic?.ClearBand();
            trackGraphic?.ClearBand();
            return;
        }

        projector.ToLocalRange(innerWorld, innerLocal);
        projector.ToLocalRange(outerWorld, outerLocal);
        borderGraphic.SetBand(innerLocal, outerLocal);
        trackGraphic.SetBand(innerLocal, outerLocal);
    }

    private void ApplyLayout()
    {
        if (rect == null) return;

        // UICamera が 16:9 にレターボックスされるため、Canvas の端がそのまま可視端になります。
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.sizeDelta = mapSize;
        // The course projection keeps its original dimensions; the bilingual header sits above it.
        rect.anchoredPosition = new Vector2(screenMargin.x, screenMargin.y + mapSize.y * .25f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void LegendMarker(Transform parent, string name, float x, Color tint)
    {
        RectTransform marker = RacingUITheme.Rect(parent, name, new Vector2(x, .062f), new Vector2(x + .05f, .11f));
        MiniMapMarkerGraphic graphic = marker.GetComponent<MiniMapMarkerGraphic>() ?? marker.gameObject.AddComponent<MiniMapMarkerGraphic>();
        graphic.color = tint;
        graphic.raycastTarget = false;
    }

    private static T EnsureGraphic<T>(RectTransform parent, string name, Color color) where T : Graphic
    {
        Transform existing = parent.Find(name);
        GameObject target = existing != null
            ? existing.gameObject
            : CreateChild(parent, name);

        T graphic = target.GetComponent<T>();
        if (graphic == null) graphic = target.AddComponent<T>();
        graphic.color = color;
        graphic.raycastTarget = false;

        RectTransform graphicRect = graphic.rectTransform;
        graphicRect.anchorMin = Vector2.zero;
        graphicRect.anchorMax = Vector2.one;
        graphicRect.offsetMin = Vector2.zero;
        graphicRect.offsetMax = Vector2.zero;
        graphicRect.pivot = new Vector2(0.5f, 0.5f);
        graphicRect.localScale = Vector3.one;
        return graphic;
    }

    private static GameObject CreateChild(Transform parent, string name, params Type[] extraComponents)
    {
        Type[] components = new Type[extraComponents.Length + 2];
        components[0] = typeof(RectTransform);
        components[1] = typeof(CanvasRenderer);
        for (int index = 0; index < extraComponents.Length; index++)
        {
            components[index + 2] = extraComponents[index];
        }

        GameObject created = new GameObject(name, components);
        created.layer = parent.gameObject.layer;
        created.transform.SetParent(parent, false);
        return created;
    }
}
