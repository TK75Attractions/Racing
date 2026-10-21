using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared campus flyover, then an individual handoff to each player's grid camera.</summary>
public sealed class CourseIntroDirector : MonoBehaviour
{
    public const float FlightSeconds = 7.25f;
    public const float HandoffSeconds = 1.75f;
    public const float Duration = FlightSeconds + HandoffSeconds;
    [Header("Campus flight path (world space)")]
    [SerializeField] private Vector3 aerial = new Vector3(360, 260, 1030);
    [SerializeField] private Vector3 approach = new Vector3(400, 215, 990);
    [SerializeField] private Vector3 descent = new Vector3(470, 85, 900);
    [SerializeField] private Vector3 schoolFront = new Vector3(475, 28, 830);
    [SerializeField] private Vector3 overviewFocus = new Vector3(510, 0, 650);
    [SerializeField] private Vector3 schoolFocus = new Vector3(500, 18, 710);
    private sealed class View
    {
        public CinemachineCamera camera;
        public CinemachineBrain brain;
        public CinemachineBlendDefinition blend;
        public CinemachineBlenderSettings customBlends;
        public CanvasGroup hud, overlay;
        public float hudAlpha;
        public Vector3 gridPosition;
        public Quaternion gridRotation;
        public float gridFov;
    }
    private readonly List<View> views = new List<View>();
    private GameObject backdrop;
    public bool IsPlaying { get; private set; }
    public float ElapsedSeconds { get; private set; }

    public Pose EvaluateFlight(float fraction)
    {
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(fraction)), u = 1 - t;
        Vector3 position = u*u*u*aerial + 3*u*u*t*approach + 3*u*t*t*descent + t*t*t*schoolFront;
        Vector3 focus = Vector3.Lerp(overviewFocus, schoolFocus, t);
        return new Pose(position, Quaternion.LookRotation(focus-position, Vector3.up));
    }

    public bool Begin(Gmanager manager, PlayerDisplayRig[] rigs)
    {
        Stop(); ElapsedSeconds = 0;
        if (!RaceFeatureController.Enabled(RaceFeature.CourseIntro)) return false;
        foreach (PlayerDisplayRig rig in rigs)
        {
            if (rig?.MainCamera == null || rig.RaceCamera == null || rig.CanvasRoot == null) continue;
            var brain = rig.MainCamera.GetComponent<CinemachineBrain>();
            Transform onPlay = rig.CanvasRoot.transform.Find("OnPlay");
            if (brain == null || onPlay == null) continue;
            var hud = onPlay.GetComponent<CanvasGroup>();
            if (hud == null) hud = onPlay.gameObject.AddComponent<CanvasGroup>();
            var go = new GameObject("Course flyover P" + (rig.PlayerIndex+1)); go.transform.SetParent(transform, false);
            var camera = go.AddComponent<CinemachineCamera>();
            camera.OutputChannel = (OutputChannels)(1 << rig.PlayerIndex); camera.Priority = 100;
            camera.Lens = rig.RaceCamera.Lens;
            var lens = camera.Lens; lens.FieldOfView = 52; lens.FarClipPlane = Mathf.Max(1800, lens.FarClipPlane); camera.Lens = lens;
            var view = new View { camera = camera, brain = brain, blend = brain.DefaultBlend, customBlends = brain.CustomBlends,
                hud = hud, hudAlpha = hud.alpha, overlay = CreateOverlay(rig, manager.GoalLap, manager.Participants.Count),
                gridPosition = rig.RaceCamera.transform.position, gridRotation = rig.RaceCamera.transform.rotation,
                gridFov = rig.RaceCamera.Lens.FieldOfView };
            views.Add(view); hud.alpha = 0;
            // Enter overhead under the existing transition's black frame, without a ground-to-sky blend.
            brain.CustomBlends = null;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
            rig.Transition?.ClearRaceStatus();
        }
        IsPlaying = views.Count > 0;
        if (IsPlaying) CreateBackdrop();
        ApplyPose();
        return IsPlaying;
    }
    public void Advance(float dt)
    {
        if (!IsPlaying) return;
        ElapsedSeconds = Mathf.Min(Duration, ElapsedSeconds + Mathf.Max(0, dt));
        ApplyPose();
        if (ElapsedSeconds >= Duration) Stop();
    }
    private void ApplyPose()
    {
        Pose flight = EvaluateFlight(ElapsedSeconds / FlightSeconds);
        float handoff = Mathf.SmoothStep(0, 1, Mathf.Clamp01((ElapsedSeconds-FlightSeconds)/HandoffSeconds));
        foreach (View view in views)
        {
            view.camera.transform.SetPositionAndRotation(Vector3.Lerp(flight.position, view.gridPosition, handoff),
                Quaternion.Slerp(flight.rotation, view.gridRotation, handoff));
            var lens = view.camera.Lens; lens.FieldOfView = Mathf.Lerp(52, view.gridFov, handoff); view.camera.Lens = lens;
            view.overlay.alpha = Mathf.Min(Mathf.Clamp01(ElapsedSeconds/.4f), Mathf.Clamp01((Duration-ElapsedSeconds)/.55f));
        }
    }
    public void Stop()
    {
        IsPlaying = false;
        foreach (View view in views)
        {
            if (view.hud != null) view.hud.alpha = view.hudAlpha;
            if (view.brain != null) { view.brain.DefaultBlend = view.blend; view.brain.CustomBlends = view.customBlends; }
            if (view.camera != null) { view.camera.enabled = false; Destroy(view.camera.gameObject); }
            if (view.overlay != null)
            {
                view.overlay.gameObject.SetActive(false);
                // Rect factories reuse children by name. Detach before deferred Destroy
                // so a same-frame restart cannot bind to the retiring overlay.
                view.overlay.transform.SetParent(null, false); Destroy(view.overlay.gameObject);
            }
        }
        views.Clear();
        if (backdrop != null) { backdrop.SetActive(false); Destroy(backdrop); backdrop = null; }
    }
    private void CreateBackdrop()
    {
        // The campus ground was authored for road-level views. Extend its material
        // for this shot only, so its rectangular edge does not float against the sky.
        var campus = GameObject.Find("1016こうしゃ");
        var ground = campus != null ? campus.transform.Find("Plane") : null;
        var source = ground != null ? ground.GetComponent<Renderer>() : null;
        if (source == null) return;
        backdrop = GameObject.CreatePrimitive(PrimitiveType.Plane); backdrop.name = "CourseIntroBackdrop";
        backdrop.transform.SetParent(transform, false);
        backdrop.transform.SetPositionAndRotation(new Vector3(500, -.35f, 650), Quaternion.identity);
        backdrop.transform.localScale = new Vector3(500, 1, 500);
        backdrop.GetComponent<Collider>().enabled = false;
        var renderer = backdrop.GetComponent<Renderer>(); renderer.sharedMaterial = source.sharedMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    private static CanvasGroup CreateOverlay(PlayerDisplayRig rig, int laps, int drivers)
    {
        var root = RacingUITheme.Rect(rig.CanvasRoot.transform, "CourseIntro", Vector2.zero, Vector2.one);
        var group = root.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false; group.alpha = 0;
        foreach (var range in new[] { new Vector4(0,0,1,.17f), new Vector4(0,.92f,1,1) })
        {
            var bar = RacingUITheme.Rect(root, range.y > .5f ? "UpperLetterbox" : "LowerLetterbox", new Vector2(range.x,range.y), new Vector2(range.z,range.w));
            var image = bar.gameObject.AddComponent<Image>(); image.color = new Color(.005f,.012f,.025f,.96f); image.raycastTarget = false;
        }
        NeonUI.Text(root, "Preview", "RACING  /  COURSE PREVIEW", new Vector2(.045f,.945f), new Vector2(.8f,.985f), 21f, tint:NeonUI.Cyan);
        NeonUI.Text(root, "Course", "つくこまサーキット", new Vector2(.045f,.068f), new Vector2(.8f,.143f), 45f, italic:true);
        NeonUI.Text(root, "Details", "TSUKUKOMA CIRCUIT   /   " + laps + " LAPS   /   " + drivers + " DRIVERS", new Vector2(.047f,.026f), new Vector2(.85f,.07f), 20f, tint:NeonUI.Cyan);
        return group;
    }
    private void OnDisable() => Stop();
}
