using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shows a compact, shape-coded participant marker above a car.</summary>
[DisallowMultipleComponent]
public sealed class CarParticipantBadge : MonoBehaviour
{
    private const int TextureWidth = 128;
    private const int TextureHeight = 64;
    private const float BaseScale = 1f;
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly Color P1Color = NeonUI.Cyan;
    private static readonly Color P2Color = NeonUI.Pink;
    private static readonly Color CpuColor = new Color(.72f, .58f, 1f, 1f);
    private static int[] playerBadgeLayers;

    private Transform badgeRoot;
    private MeshRenderer badgeRenderer;
    private Material badgeMaterial;
    private Mesh badgeMesh;
    private Texture2D badgeTexture;
    private MaterialPropertyBlock propertyBlock;
    private Camera[] sceneCameras;
    private float referenceDistance = 18f;
    private float closestDistance;

    /// <param name="participantIndex">0 = P1, 1 = P2, 2+ = CPU.</param>
    public void Configure(int participantIndex, PlayerDisplayRig[] displayRigs = null)
    {
        if (badgeRoot == null) CreateBadge(participantIndex);
        else UpdateBadgeArtwork(participantIndex);
        sceneCameras = GetVisibleCameras(displayRigs, participantIndex);

        if (participantIndex < 2)
        {
            int layer = GetPlayerBadgeLayer(participantIndex);
            badgeRoot.gameObject.layer = layer;
            badgeRenderer.gameObject.layer = layer;
            SetOwnBadgeVisibility(displayRigs, participantIndex, layer);
        }
        else
        {
            badgeRoot.gameObject.layer = gameObject.layer;
            badgeRenderer.gameObject.layer = gameObject.layer;
            // The old CPU-only TMP label is replaced by the same font-independent badge.
            Transform oldLabel = transform.Find("CPU identity");
            if (oldLabel != null) Destroy(oldLabel.gameObject);
        }
    }

    private void LateUpdate()
    {
        if (badgeRoot == null) return;
        closestDistance = 0f;
        if (sceneCameras != null)
        {
            foreach (Camera camera in sceneCameras)
            {
                if (camera == null || !camera.isActiveAndEnabled) continue;
                float distance = Vector3.Distance(camera.transform.position, badgeRoot.position);
                if (distance > closestDistance) closestDistance = distance;
            }
        }
        if (closestDistance <= 0f) closestDistance = referenceDistance;
        float multiplier = Mathf.Clamp(closestDistance / referenceDistance, 1f, 2.5f);
        badgeRoot.localScale = Vector3.one * (BaseScale * multiplier);
    }

    private void OnWillRenderObject()
    {
        Camera camera = Camera.current;
        if (camera == null || badgeRenderer == null) return;
        float distance = Vector3.Distance(camera.transform.position, badgeRoot.position);
        float alpha = Mathf.Lerp(.18f, 1f, Mathf.InverseLerp(3.5f, 9f, distance));
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        badgeRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
        propertyBlock.SetColor(ColorId, new Color(1f, 1f, 1f, alpha));
        badgeRenderer.SetPropertyBlock(propertyBlock);
    }

    private void CreateBadge(int participantIndex)
    {
        Bounds bounds = GetCarBounds();
        GameObject root = new GameObject("Participant Badge");
        root.layer = gameObject.layer;
        badgeRoot = root.transform;
        badgeRoot.SetParent(transform, false);
        badgeRoot.localPosition = new Vector3(bounds.center.x, bounds.max.y + .65f, bounds.min.z - .15f);
        badgeRoot.localRotation = Quaternion.identity;

        GameObject plate = new GameObject("Neon Plate");
        plate.layer = root.layer;
        plate.transform.SetParent(badgeRoot, false);
        badgeMesh = new Mesh { name = "Participant Badge Quad", hideFlags = HideFlags.HideAndDontSave };
        badgeMesh.vertices = new[]
        {
            new Vector3(-.5f, -.25f, 0f), new Vector3(.5f, -.25f, 0f),
            new Vector3(-.5f, .25f, 0f), new Vector3(.5f, .25f, 0f)
        };
        badgeMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        badgeMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        badgeMesh.RecalculateNormals();
        badgeMesh.RecalculateBounds();
        plate.AddComponent<MeshFilter>().sharedMesh = badgeMesh;
        badgeRenderer = plate.AddComponent<MeshRenderer>();
        badgeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        badgeRenderer.receiveShadows = false;
        badgeRenderer.lightProbeUsage = LightProbeUsage.Off;
        badgeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        badgeRenderer.sharedMaterial = CreateMaterial();
        UpdateBadgeArtwork(participantIndex);
    }

    private void UpdateBadgeArtwork(int participantIndex)
    {
        if (badgeMaterial == null) return;
        Color tint = participantIndex == 0 ? P1Color : participantIndex == 1 ? P2Color : CpuColor;
        string label = participantIndex == 0 ? "1P" : participantIndex == 1 ? "2P" : "CPU";
        if (badgeTexture != null) Destroy(badgeTexture);
        badgeTexture = CreateTexture(label, participantIndex, tint);
        badgeMaterial.SetTexture(BaseMapId, badgeTexture);
        if (badgeMaterial.HasProperty(MainTexId)) badgeMaterial.SetTexture(MainTexId, badgeTexture);
        if (badgeMaterial.HasProperty(BaseColorId)) badgeMaterial.SetColor(BaseColorId, Color.white);
        if (badgeMaterial.HasProperty(ColorId)) badgeMaterial.SetColor(ColorId, Color.white);
    }

    private Material CreateMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Transparent");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("CarParticipantBadge: no supported unlit shader was found.", this);
            return null;
        }

        badgeMaterial = new Material(shader) { name = "Participant Badge Neon", hideFlags = HideFlags.HideAndDontSave };
        if (badgeMaterial.HasProperty(SurfaceId)) badgeMaterial.SetFloat(SurfaceId, 1f);
        if (badgeMaterial.HasProperty(BlendId)) badgeMaterial.SetFloat(BlendId, 0f);
        if (badgeMaterial.HasProperty(SrcBlendId)) badgeMaterial.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
        if (badgeMaterial.HasProperty(DstBlendId)) badgeMaterial.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
        if (badgeMaterial.HasProperty(ZWriteId)) badgeMaterial.SetFloat(ZWriteId, 0f);
        badgeMaterial.SetOverrideTag("RenderType", "Transparent");
        badgeMaterial.SetShaderPassEnabled("DepthOnly", false);
        badgeMaterial.SetShaderPassEnabled("DepthNormalsOnly", false);
        badgeMaterial.SetShaderPassEnabled("ShadowCaster", false);
        badgeMaterial.renderQueue = (int)RenderQueue.Transparent;
        return badgeMaterial;
    }

    private static Texture2D CreateTexture(string label, int participantIndex, Color tint)
    {
        Texture2D texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false, true)
        {
            name = "Participant Badge " + label,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[TextureWidth * TextureHeight];
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 fill = new Color32(8, 12, 28, 242);
        Color32 border = tint;
        Color32 glyph = new Color32(255, 255, 255, 255);
        for (int y = 0; y < TextureHeight; y++)
        for (int x = 0; x < TextureWidth; x++)
        {
            bool inside = IsInsidePlate(x, y, participantIndex);
            if (!inside) { pixels[y * TextureWidth + x] = clear; continue; }
            bool edge = !IsInsidePlate(x - 2, y, participantIndex) || !IsInsidePlate(x + 2, y, participantIndex) ||
                !IsInsidePlate(x, y - 2, participantIndex) || !IsInsidePlate(x, y + 2, participantIndex);
            pixels[y * TextureWidth + x] = edge ? border : fill;
        }

        int charWidth = label.Length == 3 ? 30 : 42;
        int spacing = 4;
        int totalWidth = label.Length * charWidth + (label.Length - 1) * spacing;
        int startX = (TextureWidth - totalWidth) / 2;
        for (int i = 0; i < label.Length; i++)
        {
            string rows = Glyph(label[i]);
            for (int row = 0; row < 7; row++)
            for (int col = 0; col < 5; col++)
            {
                if (rows[row * 5 + col] != '1') continue;
                int x0 = startX + i * (charWidth + spacing) + col * (charWidth / 5);
                int y0 = 21 + (6 - row) * 4;
                for (int py = y0; py < y0 + 4; py++)
                for (int px = x0; px < x0 + charWidth / 5; px++)
                    pixels[py * TextureWidth + px] = glyph;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static bool IsInsidePlate(int x, int y, int participantIndex)
    {
        if (x < 0 || y < 0 || x >= TextureWidth || y >= TextureHeight) return false;
        if (participantIndex == 0) // P1: clipped hexagonal corners.
            return x >= 12 && x < 116 && y >= 7 && y < 57 &&
                (x >= 27 || y >= 20 && y < 44) && (x < 101 || y >= 20 && y < 44);
        if (participantIndex == 1) // P2: round plate.
        {
            float dx = (x - 63.5f) / 55f, dy = (y - 31.5f) / 25f;
            return dx * dx + dy * dy <= 1f;
        }
        return x >= 17 && x < 111 && y >= 9 && y < 55; // CPU: square plate.
    }

    private static string Glyph(char character)
    {
        switch (character)
        {
            case '1': return "00100011000010000100001000010001110";
            case '2': return "01110100010000100010001000100011111";
            case 'P': return "11110100011000111110100001000010000";
            case 'C': return "01111100001000010000100001000001111";
            case 'U': return "10001100011000110001100011000101110";
            default: return "00000000000000000000000000000000000";
        }
    }

    private static int GetPlayerBadgeLayer(int playerIndex)
    {
        if (playerBadgeLayers == null)
        {
            playerBadgeLayers = new[] { -1, -1 };
            int next = 31;
            for (int player = 0; player < 2; player++)
            {
                while (next >= 8 && !string.IsNullOrEmpty(LayerMask.LayerToName(next))) next--;
                playerBadgeLayers[player] = next >= 8 ? next-- : 0;
            }
        }
        return playerBadgeLayers[Mathf.Clamp(playerIndex, 0, 1)];
    }

    private static void SetOwnBadgeVisibility(PlayerDisplayRig[] rigs, int playerIndex, int layer)
    {
        if (rigs == null) return;
        int bit = 1 << layer;
        foreach (PlayerDisplayRig rig in rigs)
        {
            if (rig == null || rig.PlayerIndex != playerIndex) continue;
            SetLayerMask(rig.CameraRoot, bit);
            SetLayerMask(rig.BackCameraRoot, bit);
        }
    }

    private static Camera[] GetVisibleCameras(PlayerDisplayRig[] rigs, int participantIndex)
    {
        if (rigs == null || rigs.Length == 0) return Camera.allCameras;
        var cameras = new System.Collections.Generic.List<Camera>();
        foreach (PlayerDisplayRig rig in rigs)
        {
            if (rig == null || (participantIndex < 2 && rig.PlayerIndex == participantIndex)) continue;
            AddCameras(cameras, rig.CameraRoot);
            AddCameras(cameras, rig.BackCameraRoot);
        }
        return cameras.ToArray();
    }

    private static void AddCameras(System.Collections.Generic.List<Camera> cameras, GameObject root)
    {
        if (root == null) return;
        foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            if (camera != null && !cameras.Contains(camera)) cameras.Add(camera);
    }

    private static void SetLayerMask(GameObject root, int bit)
    {
        if (root == null) return;
        foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            camera.cullingMask &= ~bit;
    }

    private Bounds GetCarBounds()
    {
        Bounds bounds = new Bounds();
        bool found = false;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (badgeRoot != null && renderer.transform.IsChildOf(badgeRoot)) continue;
            Bounds local = renderer.localBounds;
            Matrix4x4 matrix = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z));
                if (found) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        return found ? bounds : new Bounds(new Vector3(0f, .6f, 0f), new Vector3(2f, 1.2f, 4f));
    }

    private void OnDestroy()
    {
        if (badgeRoot != null) Destroy(badgeRoot.gameObject);
        if (badgeMaterial != null) Destroy(badgeMaterial);
        if (badgeMesh != null) Destroy(badgeMesh);
        if (badgeTexture != null) Destroy(badgeTexture);
    }
}
