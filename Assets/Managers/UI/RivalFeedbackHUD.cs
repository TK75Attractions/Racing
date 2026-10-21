using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Per-display rival awareness and confirmed outgoing item results. No vehicle control changes.</summary>
[DefaultExecutionOrder(900)]
[DisallowMultipleComponent]
public sealed class RivalFeedbackHUD : MonoBehaviour
{
    private struct Notice { public CarItemEffects target; public ItemImpactKind kind; public string text; }
    private readonly Queue<Notice> pending = new Queue<Notice>(3);
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private Gmanager manager;
    private int playerIndex;
    private Camera view;
    private RaceParticipant own, front, rear;
    private CarItemEffects bound;
    private RectTransform root, frontPlate, rearPlate, hitPlate;
    private TMP_Text frontText, rearText, hitText;
    private CanvasGroup frontGroup, rearGroup, hitGroup;
    private RivalWarningGraphic edge;
    private GameObject arrow;
    private Mesh arrowMesh;
    private MeshRenderer arrowRenderer;
    private Material arrowMaterial;
    private float nextScan, frontStrength, rearStrength, rearAlpha, noticeUntil;
    private Notice current;
    public bool FrontVisible => front != null && frontGroup != null && frontGroup.alpha > 0f;
    public bool RearVisible => rear != null && rearAlpha > .01f;
    public int FrontTargetIndex => front != null ? front.Index : -1;
    public int RearTargetIndex => rear != null ? rear.Index : -1;
    public string HitNotice => hitGroup != null && hitGroup.alpha > 0f ? hitText.text : "";
    public int ConfirmedNotices { get; private set; }
    public Renderer ArrowRenderer => arrowRenderer;

    public void Configure(Gmanager race, int index, PlayerDisplayRig rig)
    {
        manager = race; playerIndex = index; view = rig.MainCamera;
        root = RacingUITheme.Rect(transform, "RivalFeedback", Vector2.zero, Vector2.one);
        Transform onPlay = transform.Find("OnPlay");
        if (onPlay != null) root.SetSiblingIndex(onPlay.GetSiblingIndex() + 1);
        var edgeRect = RacingUITheme.Rect(root,"RearWarningEdges",Vector2.zero,Vector2.one);
        edge = edgeRect.gameObject.AddComponent<RivalWarningGraphic>(); edge.color = Color.clear;
        frontPlate = Plate("FrontRival",new Vector2(.36f,.65f),new Vector2(.64f,.705f),new Color(1f,.72f,.2f),out frontText,out frontGroup);
        rearPlate = Plate("RearRival",new Vector2(.35f,.23f),new Vector2(.65f,.29f),RacingHUDStyle.Alert,out rearText,out rearGroup);
        hitPlate = Plate("ItemHitConfirmed",new Vector2(.32f,.335f),new Vector2(.68f,.415f),RacingHUDStyle.Teal,out hitText,out hitGroup);
        CreateArrow();
        ClearPresentation();
    }
    private RectTransform Plate(string name, Vector2 min, Vector2 max, Color accent, out TMP_Text text, out CanvasGroup group)
    {
        var plate = RacingHUDStyle.Plate(root,name,min,max,RacingHUDPlateGraphic.PlateShape.Notification,accent);
        text = RacingHUDStyle.Label(plate,"Text","",.035f,.10f,.965f,.90f,28,accent,TextAlignmentOptions.Center,true);
        text.richText = false;
        group = plate.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
        return plate;
    }
    private void OnEnable() { RenderPipelineManager.beginCameraRendering += BeforeCamera; }
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        Bind(null); ClearPresentation();
    }
    private void OnDestroy()
    {
        Bind(null);
        if (root != null) Destroy(root.gameObject);
        if (arrow != null) Destroy(arrow);
        if (arrowMesh != null) Destroy(arrowMesh);
        if (arrowMaterial != null) Destroy(arrowMaterial);
    }
    private void Bind(CarItemEffects target)
    {
        if (bound == target) return;
        if (bound != null) bound.AttackLanded -= OnAttackLanded;
        bound = target;
        if (bound != null) bound.AttackLanded += OnAttackLanded;
        pending.Clear(); current = default; noticeUntil = 0;
    }
    private void LateUpdate()
    {
        if (root == null || manager == null) return;
        own = manager.GetParticipant(playerIndex);
        bool racing = manager.state == Gmanager.State.Game && own != null && own.IsRacing && own.Car.activeInHierarchy;
        Bind(racing ? own.Effects : null);
        if (!racing) { ClearPresentation(); return; }
        root.gameObject.SetActive(true);
        if (!RaceFeatureController.Enabled(RaceFeature.RivalProximity)) ClearProximity();
        else
        {
            if (Time.time >= nextScan) { nextScan = Time.time + .10f; Scan(); }
            UpdateProximity();
        }
        UpdateNotice();
    }
    private void Scan()
    {
        front = rear = null; frontStrength = rearStrength = 0;
        if (manager.course == null || !manager.course.HasValidPath || own.Effects != null && own.Effects.IsRecoveryProtected) return;
        Vector3 forward = own.Body.rotation * Vector3.forward, up = own.Body.rotation * Vector3.up, right = own.Body.rotation * Vector3.right;
        float progress = manager.course.GetProgressDistance(own.Body.position);
        foreach (var candidate in manager.Participants)
        {
            if (candidate == own || !candidate.IsRacing || !candidate.Car.activeInHierarchy ||
                candidate.Effects != null && candidate.Effects.IsRecoveryProtected) continue;
            Vector3 delta = candidate.Body.position - own.Body.position;
            float distance = delta.magnitude;
            if (distance > 55f) continue;
            float along = Vector3.Dot(delta,forward);
            bool behind = along < 0;
            float closing = Vector3.Dot(own.Body.linearVelocity - candidate.Body.linearVelocity,forward) * (behind ? -1 : 1);
            float gap = RivalProximity.ShortestCourseGap(progress,manager.course.GetProgressDistance(candidate.Body.position),manager.course.TotalLength);
            float strength = RivalProximity.Strength(along,Vector3.Dot(delta,right),Vector3.Dot(delta,up),gap,distance,closing,behind,true);
            if (strength <= 0 || !ClearSight(candidate)) continue;
            if (behind && strength > rearStrength) { rear = candidate; rearStrength = strength; }
            if (!behind && strength > frontStrength) { front = candidate; frontStrength = strength; }
        }
        frontText.text = front != null ? "前方に " + front.Label + "  /  " + Mathf.RoundToInt(Vector3.Distance(own.Body.position,front.Body.position)) + " m" : "";
        rearText.text = rear != null ? rear.Label + " が後方から接近！" : "";
    }
    private bool ClearSight(RaceParticipant target)
    {
        Vector3 up = own.Body.rotation * Vector3.up;
        Vector3 start = own.Body.worldCenterOfMass + up * .5f;
        Vector3 delta = target.Body.worldCenterOfMass + up * .5f - start;
        if (delta.sqrMagnitude < .001f) return true;
        int count = Physics.RaycastNonAlloc(start,delta.normalized,sightHits,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i];
            if (hit.rigidbody == own.Body || hit.rigidbody == target.Body) continue;
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;
            return false;
        }
        return true;
    }
    private void UpdateProximity()
    {
        if (front != null && !front.IsRacing) { front = null; frontStrength = 0; }
        if (rear != null && !rear.IsRacing) { rear = null; rearStrength = 0; }
        frontGroup.alpha = front != null ? .85f + .15f * frontStrength : 0;
        rearAlpha = Mathf.MoveTowards(rearAlpha,rearStrength,Time.deltaTime * 3.5f);
        rearGroup.alpha = rear != null ? rearAlpha : 0;
        edge.color = new Color(.96f,.06f,.09f,rearAlpha * (.65f + .04f * Mathf.Sin(Time.time * 5f)));
        arrow.SetActive(front != null);
        if (front == null) return;
        Vector3 up = own.Body.rotation * Vector3.up;
        Vector3 anchor = front.Body.position + up * (2.4f + .10f * Mathf.Sin(Time.time * 3f));
        if (view != null)
        {
            // A nearby rival can fill the view. Keep the 3D cue inside the frame instead of above its top edge.
            Vector3 screen = view.WorldToViewportPoint(anchor);
            screen.x = Mathf.Clamp(screen.x,.30f,.88f);
            screen.y = Mathf.Clamp(screen.y,.36f,.75f);
            screen.z = Mathf.Max(6f,screen.z);
            anchor = view.ViewportToWorldPoint(screen);
        }
        Vector3 direction = front.Body.worldCenterOfMass - anchor;
        if (direction.sqrMagnitude < .01f) direction = -up;
        Vector3 face = view != null ? view.transform.position - anchor : own.Body.position - anchor;
        // Point at the actual rival, with the broad face turned towards the driver. A horizontal
        // arrow becomes an unreadable thin line when camera and cue are at the same height.
        face = Vector3.ProjectOnPlane(face,direction);
        if (face.sqrMagnitude < .01f) face = own.Body.rotation * Vector3.forward;
        arrow.transform.SetPositionAndRotation(anchor,Quaternion.LookRotation(direction,face));
        float size = Mathf.Clamp(Vector3.Distance(front.Body.position,own.Body.position) * .065f,.85f,2.3f);
        arrow.transform.localScale = Vector3.one * size;
    }
    private void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        // A world-space mesh is submitted exclusively to its owner's main view, including render-request captures.
        if (arrowRenderer != null) arrowRenderer.enabled = isActiveAndEnabled && camera == view && arrow.activeSelf;
    }
    private void OnAttackLanded(CarItemEffects target, ItemImpactKind kind)
    {
        if (!RaceFeatureController.Enabled(RaceFeature.ItemHitFeedback) || manager.state != Gmanager.State.Game || own == null || !own.IsRacing) return;
        RaceParticipant victim = null;
        foreach (var entry in manager.Participants) if (entry.Effects == target) { victim = entry; break; }
        if (victim == null || victim == own || !victim.IsRacing) return;
        string name = victim.Label;
        string text = kind == ItemImpactKind.OilSlip ? name + " がオイルでスリップ！" :
            kind == ItemImpactKind.ShieldSpin ? name + " をスピンさせた！" :
            kind == ItemImpactKind.SteeringReversed ? name + " のハンドルを反転！" : name + " が反転中に壁へ接触！";
        var notice = new Notice { target = target, kind = kind, text = text };
        ConfirmedNotices++;
        // Upgrade this victim's active reversal notice rather than adding a second delayed message.
        if (kind == ItemImpactKind.ConfusedWallContact && current.target == target && Time.time < noticeUntil)
        { ShowNotice(notice); return; }
        if (Time.time >= noticeUntil) ShowNotice(notice);
        else { if (pending.Count == 3) pending.Dequeue(); pending.Enqueue(notice); }
    }
    private void ShowNotice(Notice notice)
    {
        current = notice; hitText.text = notice.text; noticeUntil = Time.time + 2.6f;
        hitGroup.alpha = 1;
    }
    private void UpdateNotice()
    {
        if (!RaceFeatureController.Enabled(RaceFeature.ItemHitFeedback))
        { pending.Clear(); noticeUntil = 0; current = default; hitGroup.alpha = 0; return; }
        if (Time.time >= noticeUntil && pending.Count > 0) ShowNotice(pending.Dequeue());
        hitGroup.alpha = Mathf.Clamp01((noticeUntil - Time.time) / .35f);
    }
    private void ClearProximity()
    {
        front = rear = null; frontStrength = rearStrength = rearAlpha = 0; nextScan = 0;
        if (frontGroup != null) frontGroup.alpha = 0;
        if (rearGroup != null) rearGroup.alpha = 0;
        if (edge != null) edge.color = Color.clear;
        if (arrow != null) arrow.SetActive(false);
    }
    private void ClearPresentation()
    {
        ClearProximity(); pending.Clear(); current = default; noticeUntil = 0;
        if (hitGroup != null) hitGroup.alpha = 0;
        if (root != null) root.gameObject.SetActive(false);
    }
    private void CreateArrow()
    {
        arrow = new GameObject("Front rival 3D arrow P" + (playerIndex + 1));
        // Canvas roots use a tiny world scale. The actual 3D cue must retain metre-sized geometry.
        Vector2[] outline = { new Vector2(-.22f,-1.15f),new Vector2(.22f,-1.15f),new Vector2(.22f,.12f),new Vector2(.85f,.12f),new Vector2(0,1.2f),new Vector2(-.85f,.12f),new Vector2(-.22f,.12f) };
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i], b = outline[(i+1)%outline.Length];
            AddTriangle(vertices,triangles,new Vector3(0,.16f,0),new Vector3(b.x,.16f,b.y),new Vector3(a.x,.16f,a.y));
            AddTriangle(vertices,triangles,Vector3.zero,new Vector3(a.x,0,a.y),new Vector3(b.x,0,b.y));
            AddTriangle(vertices,triangles,new Vector3(a.x,0,a.y),new Vector3(a.x,.16f,a.y),new Vector3(b.x,.16f,b.y));
            AddTriangle(vertices,triangles,new Vector3(a.x,0,a.y),new Vector3(b.x,.16f,b.y),new Vector3(b.x,0,b.y));
        }
        arrowMesh = new Mesh { name = "Extruded rival direction arrow" };
        arrowMesh.SetVertices(vertices); arrowMesh.SetTriangles(triangles,0); arrowMesh.RecalculateNormals(); arrowMesh.RecalculateBounds();
        arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
        arrowRenderer = arrow.AddComponent<MeshRenderer>();
        arrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Rival arrow amber" };
        arrowMaterial.color = new Color(1f,.62f,.06f); arrowMaterial.EnableKeyword("_EMISSION"); arrowMaterial.SetColor("_EmissionColor",new Color(.6f,.22f,.015f));
        arrowRenderer.sharedMaterial = arrowMaterial; arrowRenderer.shadowCastingMode = ShadowCastingMode.Off; arrowRenderer.receiveShadows = false;
        arrowRenderer.enabled = false;
    }
    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
        triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2);
    }
}
