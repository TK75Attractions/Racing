using UnityEngine;

/// <summary>車体色をプレイヤーごとに設定し、元の共有マテリアルには触れない。</summary>
[DisallowMultipleComponent]
public sealed class PlayerCarPaint : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private readonly System.Collections.Generic.List<Material> ownedMaterials =
        new System.Collections.Generic.List<Material>();

    public static Color GetPlayerColor(int playerIndex) =>
        playerIndex == 1 ? new Color(1f, .055f, .34f, 1f) : new Color(.035f, .28f, 1f, 1f);

    public void SetPlayerIndex(int playerIndex)
    {
        Color color = GetPlayerColor(Mathf.Clamp(playerIndex, 0, 1));
        int paintedSlots = 0;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer)
                continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null || !IsBodyPaint(source.name)) continue;

                Material material = new Material(source);
                material.name = source.name + (playerIndex == 1 ? " (Player 2 Pink)" : " (Player 1 Blue)");
                if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
                if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
                if (material.HasProperty(EmissionColorId))
                {
                    material.SetColor(EmissionColorId, color * .035f);
                    material.EnableKeyword("_EMISSION");
                }

                ownedMaterials.Add(material);
                materials[i] = material;
                paintedSlots++;
                changed = true;
            }

            if (changed) renderer.sharedMaterials = materials;
        }

        if (paintedSlots == 0)
            Debug.LogWarning("PlayerCarPaint: no body paint materials were found on this car.", this);
    }

    private static bool IsBodyPaint(string materialName)
    {
        string name = materialName.Replace(" (Instance)", string.Empty);
        return name.StartsWith("Body1", System.StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("CarPaint", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("BodyPaint", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnDestroy()
    {
        foreach (Material material in ownedMaterials)
            if (material != null) Destroy(material);
        ownedMaterials.Clear();
    }
}
