using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Slow campus establishing shots on each player's title and results, using existing cameras.</summary>
[DefaultExecutionOrder(-100)]
public sealed class MenuSceneryDirector : MonoBehaviour
{
    private sealed class View
    {
        public PlayerDisplayRig rig;
        public CinemachineCamera camera;
        public Pose original;
        public LensSettings lens;
        public RawImage titleArt, resultArt;
        public Transform titleScrim, resultScrim, brand;
        public bool showing;
        public Gmanager.State phase;
        public float started;
    }
    private readonly List<View> views = new List<View>();
    private Gmanager manager;
    private GameObject backdrop;
    private Renderer ground, backdropRenderer;
    public bool IsShowing { get; private set; }

    public void Configure(Gmanager race, PlayerDisplayRig[] rigs, CinemachineCamera[] cameras)
    {
        manager = race;
        for (int i = 0; i < rigs.Length && i < cameras.Length; i++)
        {
            if (rigs[i]?.CanvasRoot == null || cameras[i] == null) continue;
            Transform title = rigs[i].CanvasRoot.transform.Find("Title");
            Transform result = rigs[i].CanvasRoot.transform.Find("Result/ResultPresentation");
            views.Add(new View { rig = rigs[i], camera = cameras[i],
                original = new Pose(cameras[i].transform.position, cameras[i].transform.rotation), lens = cameras[i].Lens,
                titleArt = title?.Find("TitleBackground")?.GetComponent<RawImage>(),
                resultArt = result?.Find("ResultBackground")?.GetComponent<RawImage>(),
                titleScrim = title?.Find("MenuSceneryScrim"), resultScrim = result?.Find("MenuSceneryScrim"),
                brand = title?.Find("SceneryTitleBrand") });
        }
        var campus = GameObject.Find("1016こうしゃ");
        ground = campus != null ? campus.transform.Find("Plane")?.GetComponent<Renderer>() : null;
        if (ground != null)
        {
            backdrop = GameObject.CreatePrimitive(PrimitiveType.Plane);
            backdrop.name = "MenuSceneryBackdrop";
            backdrop.transform.SetParent(transform, false);
            backdrop.transform.SetPositionAndRotation(new Vector3(500f, -.4f, 650f), Quaternion.identity);
            backdrop.transform.localScale = new Vector3(500f, 1f, 500f);
            // No additional terrain collisions are needed for a menu-only ground extension.
            Collider collider = backdrop.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            backdropRenderer = backdrop.GetComponent<Renderer>();
            backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backdrop.SetActive(false);
        }
        Refresh();
    }

    public static Pose EvaluatePose(bool results, float seconds)
    {
        float angle = seconds * Mathf.PI * 2f / 120f;
        Vector3 position = results ? new Vector3(620f, 130f, 940f) : new Vector3(360f, 220f, 1030f);
        position += new Vector3(Mathf.Sin(angle) * (results ? 45f : 70f), Mathf.Sin(angle * 2f) * 12f, (Mathf.Cos(angle) - 1f) * 30f);
        Vector3 focus = results ? new Vector3(460f, 12f, 735f) : new Vector3(510f, 12f, 690f);
        return new Pose(position, Quaternion.LookRotation(focus - position, Vector3.up));
    }

    private void LateUpdate() => Refresh();
    public void Refresh()
    {
        if (manager == null) return;
        bool scenic = isActiveAndEnabled && manager.Features != null && manager.Features.Settings.IsEnabled(RaceFeature.MenuScenery);
        IsShowing = false;
        foreach (View view in views)
        {
            // Preparation and returning to the title can progress independently on the two displays.
            Gmanager.State phase = manager.IsPreparing ? manager.GetPlayerPreparationState(view.rig.PlayerIndex) :
                manager.state == Gmanager.State.Result ? manager.GetPlayerAudioState(view.rig.PlayerIndex) : manager.state;
            bool show = scenic && (phase == Gmanager.State.Title || phase == Gmanager.State.CarSelection || phase == Gmanager.State.Result);
            SetPresentation(view, scenic);
            if (show)
            {
                if (!view.showing || view.phase != phase) view.started = Time.unscaledTime;
                Pose pose = EvaluatePose(phase == Gmanager.State.Result, Time.unscaledTime - view.started);
                view.camera.ForceCameraPosition(pose.position, pose.rotation);
                LensSettings lens = view.lens;
                lens.FieldOfView = phase == Gmanager.State.Result ? 52f : 50f;
                lens.FarClipPlane = Mathf.Max(2200f, lens.FarClipPlane);
                view.camera.Lens = lens;
                view.camera.Priority = 20;
                view.rig.RaceCamera.Priority = 10;
                IsShowing = true;
            }
            else if (view.showing)
            {
                Restore(view);
                bool legacyTitle = phase == Gmanager.State.Title || phase == Gmanager.State.CarSelection;
                view.camera.Priority = legacyTitle ? 20 : 10;
                view.rig.RaceCamera.Priority = legacyTitle ? 10 : 20;
            }
            view.showing = show;
            view.phase = phase;
        }
        if (backdrop != null)
        {
            if (backdrop.activeSelf != IsShowing) backdrop.SetActive(IsShowing);
            if (ground != null && backdropRenderer.sharedMaterial != ground.sharedMaterial)
                backdropRenderer.sharedMaterial = ground.sharedMaterial;
        }
    }

    private static void SetPresentation(View view, bool scenic)
    {
        if (view.titleArt != null) view.titleArt.enabled = !scenic;
        if (view.resultArt != null) view.resultArt.enabled = !scenic;
        if (view.titleScrim != null && view.titleScrim.gameObject.activeSelf != scenic) view.titleScrim.gameObject.SetActive(scenic);
        if (view.resultScrim != null && view.resultScrim.gameObject.activeSelf != scenic) view.resultScrim.gameObject.SetActive(scenic);
        if (view.brand != null && view.brand.gameObject.activeSelf != scenic) view.brand.gameObject.SetActive(scenic);
    }
    private static void Restore(View view)
    {
        if (view.camera == null) return;
        view.camera.ForceCameraPosition(view.original.position, view.original.rotation);
        view.camera.Lens = view.lens;
    }
    private void OnDisable()
    {
        foreach (View view in views)
        {
            if (view.showing) Restore(view);
            view.showing = false;
            SetPresentation(view, false);
            bool title = view.phase == Gmanager.State.Title || view.phase == Gmanager.State.CarSelection;
            if (view.camera != null) view.camera.Priority = title ? 20 : 10;
            if (view.rig?.RaceCamera != null) view.rig.RaceCamera.Priority = title ? 10 : 20;
        }
        IsShowing = false;
        if (backdrop != null) backdrop.SetActive(false);
    }
    private void OnDestroy() { if (backdrop != null) Destroy(backdrop); }
}
