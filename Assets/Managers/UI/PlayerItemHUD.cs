using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤー画面ごとのアイテム表示です。取得・被弾時の告知、効果中の残り時間、
/// オイルを踏んだときの画面の油汚れ、コンフューズ中の画面縁の色を担当します。
/// </summary>
public sealed class PlayerItemHUD : MonoBehaviour
{
    private const int OilBlobCount = 9;
    private const float ToastSeconds = 1.6f;
    private const float ToastFadeSeconds = 0.35f;
    private const float OilExtraFadeSeconds = 1.2f;

    private static readonly Color ShieldColor = new Color(0.3f, 0.88f, 1f, 1f);
    private static readonly Color RocketColor = new Color(1f, 0.55f, 0.15f, 1f);
    private static readonly Color OilColor = new Color(0.95f, 0.72f, 0.3f, 1f);
    private static readonly Color ConfuseColor = new Color(1f, 0.3f, 0.85f, 1f);
    private static readonly Color ChargeColor = new Color(0.4f, 0.95f, 1f, 1f);
    private static readonly Color WarningColor = new Color(1f, 0.35f, 0.3f, 1f);

    private static Sprite[] oilSprites;
    private static Sprite edgeSprite;

    private RectTransform root;
    private CanvasGroup toastGroup;
    private RectTransform toastRect;
    private TMP_Text toastTitle;
    private TMP_Text toastCaption;
    private TMP_Text statusLabel;
    private Image confuseFrame;
    private Image oilTint;
    private readonly Image[] oilBlobs = new Image[OilBlobCount];
    private readonly Vector2[] oilBlobDrift = new Vector2[OilBlobCount];
    private CarItemEffects effects;
    private float toastAge = float.PositiveInfinity;
    private float oilAge = float.PositiveInfinity;
    private float oilHoldSeconds = 1.5f;

    /// <summary>キャンバスのレース表示（OnPlay）の直後に HUD を作ります。</summary>
    public static PlayerItemHUD Create(Transform canvasRoot)
    {
        if (canvasRoot == null) return null;
        PlayerItemHUD hud = canvasRoot.GetComponent<PlayerItemHUD>();
        if (hud == null) hud = canvasRoot.gameObject.AddComponent<PlayerItemHUD>();
        hud.Build(canvasRoot);
        hud.Bind(null);
        return hud;
    }

    /// <summary>表示対象の車を切り替えます。null で非表示にします。</summary>
    public void Bind(CarItemEffects target)
    {
        if (effects != null)
        {
            effects.ItemUsed -= HandleItemUsed;
            effects.Afflicted -= HandleAfflicted;
            effects.ChargeCollected -= HandleChargeCollected;
        }

        effects = target;
        if (effects != null)
        {
            effects.ItemUsed += HandleItemUsed;
            effects.Afflicted += HandleAfflicted;
            effects.ChargeCollected += HandleChargeCollected;
        }

        toastAge = float.PositiveInfinity;
        oilAge = float.PositiveInfinity;
        Refresh(0f);
    }

    private void OnDestroy()
    {
        Bind(null);
    }

    private void Update()
    {
        Refresh(Time.deltaTime);
    }

    private void Refresh(float deltaTime)
    {
        if (root == null) return;
        bool visible = effects != null && effects.isActiveAndEnabled;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return;

        int siblingIndex = GetTargetSiblingIndex();
        if (root.GetSiblingIndex() != siblingIndex) root.SetSiblingIndex(siblingIndex);
        UpdateToast(deltaTime);
        UpdateOil(deltaTime);
        UpdateConfuse();
        UpdateStatus();
    }

    private void HandleItemUsed(RaceItemType item)
    {
        switch (item)
        {
            case RaceItemType.Shield:
                ShowToast("シールド", "SHIELD  —  相手に当たるとスピン", ShieldColor);
                break;
            case RaceItemType.Rocket:
                ShowToast("ロケット", "ROCKET  —  自動操縦で超加速", RocketColor);
                break;
            case RaceItemType.Oil:
                ShowToast("オイル", "OIL  —  後ろに設置", OilColor);
                break;
            case RaceItemType.Confuse:
                ShowToast("コンフューズ", "CONFUSE  —  相手のハンドルを反転", ConfuseColor);
                break;
        }
    }

    private void HandleAfflicted(RaceItemType item)
    {
        switch (item)
        {
            case RaceItemType.Oil:
                ShowToast("オイルを踏んだ!", "SLIPPING", WarningColor);
                StartOilSplatter();
                break;
            case RaceItemType.Confuse:
                ShowToast("ハンドル反転!", "CONFUSED", ConfuseColor);
                break;
            case RaceItemType.Shield:
                ShowToast("スピン!", "SPUN BY SHIELD", WarningColor);
                break;
        }
    }

    private void HandleChargeCollected()
    {
        ShowToast("チャージ MAX", "DRIFT CHARGE  —  ドリフト解放で加速", ChargeColor);
    }

    private void ShowToast(string title, string caption, Color tint)
    {
        if (toastTitle == null) return;
        toastTitle.text = title;
        toastTitle.color = tint;
        toastCaption.text = caption;
        toastAge = 0f;
    }

    private void UpdateToast(float deltaTime)
    {
        toastAge += deltaTime;
        if (toastAge >= ToastSeconds)
        {
            toastGroup.alpha = 0f;
            return;
        }

        // 出現時に少し大きく表示してから戻し、最後はフェードアウトします。
        float pop = Mathf.Clamp01(toastAge / 0.15f);
        toastRect.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, 1f - (1f - pop) * (1f - pop));
        float fade = Mathf.Clamp01((ToastSeconds - toastAge) / ToastFadeSeconds);
        toastGroup.alpha = fade;
    }

    private void StartOilSplatter()
    {
        oilAge = 0f;
        oilHoldSeconds = effects != null ? Mathf.Max(0.3f, effects.OilDuration * 0.6f) : 1.5f;
        float height = Mathf.Max(1f, root.rect.height);
        float width = Mathf.Max(1f, root.rect.width);
        for (int i = 0; i < oilBlobs.Length; i++)
        {
            Image blob = oilBlobs[i];
            blob.sprite = oilSprites[Random.Range(0, oilSprites.Length)];
            RectTransform rect = blob.rectTransform;
            // 中央付近を少し避け、視界の端から汚れが広がるように配置します。
            float x = Random.value < 0.5f ? Random.Range(0.04f, 0.4f) : Random.Range(0.6f, 0.96f);
            if (i < 2) x = Random.Range(0.3f, 0.7f);
            float y = Random.Range(0.2f, 0.95f);
            rect.anchoredPosition = new Vector2((x - 0.5f) * width, (y - 0.5f) * height);
            float size = height * Random.Range(0.18f, i < 2 ? 0.3f : 0.42f);
            rect.sizeDelta = new Vector2(size, size * Random.Range(0.85f, 1.2f));
            rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            oilBlobDrift[i] = new Vector2(Random.Range(-0.004f, 0.004f), -Random.Range(0.01f, 0.05f)) * height;
            blob.color = Color.white;
            blob.enabled = true;
        }
    }

    private void UpdateOil(float deltaTime)
    {
        float totalSeconds = oilHoldSeconds + OilExtraFadeSeconds + (effects != null ? effects.OilDuration * 0.4f : 1f);
        oilAge += deltaTime;
        bool active = oilAge < totalSeconds;
        float alpha = active ? Mathf.Clamp01(1f - (oilAge - oilHoldSeconds) / (totalSeconds - oilHoldSeconds)) : 0f;

        // 踏んだ瞬間は画面全体を一瞬暗くして、油を浴びた感じを出します。
        float flash = active ? Mathf.Clamp01(1f - oilAge / 0.45f) : 0f;
        oilTint.color = new Color(0.08f, 0.05f, 0.02f, 0.55f * flash);
        oilTint.enabled = flash > 0f;

        for (int i = 0; i < oilBlobs.Length; i++)
        {
            Image blob = oilBlobs[i];
            blob.enabled = active;
            if (!active) continue;
            // 時間とともにゆっくり垂れ落ちます。
            blob.rectTransform.anchoredPosition += oilBlobDrift[i] * deltaTime;
            Color color = blob.color;
            color.a = alpha;
            blob.color = color;
        }
    }

    private void UpdateConfuse()
    {
        bool confused = effects.IsConfused;
        confuseFrame.enabled = confused;
        if (!confused) return;
        float pulse = 0.55f + 0.35f * Mathf.Sin(Time.time * 10f);
        Color color = ConfuseColor;
        color.a = pulse;
        confuseFrame.color = color;
    }

    private void UpdateStatus()
    {
        string text = string.Empty;
        Color tint = Color.white;
        if (effects.IsRocketActive)
        {
            text = $"ROCKET  自動操縦  {effects.RocketTimeRemaining:0.0}";
            tint = RocketColor;
        }
        else if (effects.IsSpinning)
        {
            text = "SPIN";
            tint = WarningColor;
        }
        else if (effects.IsConfused)
        {
            text = $"ハンドル反転  {effects.ConfuseTimeRemaining:0.0}";
            tint = ConfuseColor;
        }
        else if (effects.IsOiled)
        {
            text = "SLIPPING";
            tint = OilColor;
        }
        else if (effects.IsShieldActive)
        {
            text = $"SHIELD  {effects.ShieldTimeRemaining:0.0}";
            tint = ShieldColor;
        }

        statusLabel.text = text;
        statusLabel.color = tint;
    }

    private int GetTargetSiblingIndex()
    {
        // OnPlay の HUD の上、画面遷移やメニューの下に置きます。
        Transform parent = root.parent;
        Transform onPlay = parent.Find("OnPlay");
        int target = onPlay != null ? onPlay.GetSiblingIndex() + 1 : 0;
        if (root.GetSiblingIndex() < target) target--;
        return Mathf.Clamp(target, 0, parent.childCount - 1);
    }

    private void Build(Transform canvasRoot)
    {
        EnsureSprites();
        Transform existing = canvasRoot.Find("ItemHUD");
        if (existing != null)
        {
            // Destroy はフレーム末まで遅れるため、名前を変えて新しい HUD と取り違えないようにします。
            existing.name = "ItemHUD (Removed)";
            Destroy(existing.gameObject);
        }

        root = RacingUITheme.Rect(canvasRoot, "ItemHUD", Vector2.zero, Vector2.one);
        root.SetSiblingIndex(GetTargetSiblingIndex());

        oilTint = CreateImage(root, "OilFlash", null, Vector2.zero, Vector2.one);
        RectTransform oilLayer = RacingUITheme.Rect(root, "OilSplatter", Vector2.zero, Vector2.one);
        for (int i = 0; i < oilBlobs.Length; i++)
        {
            Image blob = CreateImage(oilLayer, $"OilBlob{i}", oilSprites[i % oilSprites.Length],
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            blob.enabled = false;
            oilBlobs[i] = blob;
        }

        confuseFrame = CreateImage(root, "ConfuseFrame", edgeSprite, Vector2.zero, Vector2.one);
        confuseFrame.enabled = false;

        toastRect = RacingUITheme.Rect(root, "Toast", new Vector2(0.2f, 0.62f), new Vector2(0.8f, 0.8f));
        toastGroup = toastRect.gameObject.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGroup.blocksRaycasts = false;
        toastTitle = RacingUITheme.Label(toastRect, "Title", string.Empty, new Vector2(0f, 0.38f), new Vector2(1f, 1f),
            64f, Color.white, FontRole.Japanese, TextAlignmentOptions.Center);
        toastTitle.fontStyle = FontStyles.Bold;
        toastTitle.outlineWidth = 0.18f;
        toastTitle.outlineColor = new Color32(0, 0, 0, 200);
        toastCaption = RacingUITheme.Label(toastRect, "Caption", string.Empty, new Vector2(0f, 0f), new Vector2(1f, 0.36f),
            24f, RacingUITheme.Text, FontRole.Japanese, TextAlignmentOptions.Center);

        statusLabel = RacingUITheme.Label(root, "Status", string.Empty, new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.26f),
            30f, Color.white, FontRole.Japanese, TextAlignmentOptions.Center);
        statusLabel.fontStyle = FontStyles.Bold;
    }

    private static Image CreateImage(Transform parent, string name, Sprite sprite, Vector2 min, Vector2 max)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, min, max);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }

    private static void EnsureSprites()
    {
        if (oilSprites == null)
        {
            oilSprites = new Sprite[3];
            for (int i = 0; i < oilSprites.Length; i++) oilSprites[i] = CreateOilSprite(1000 + i * 17);
        }

        if (edgeSprite == null) edgeSprite = CreateEdgeSprite();
    }

    /// <summary>黒い油の飛沫です。輪郭の揺らぎ、周囲の小さな飛沫、光沢のハイライトを持ちます。</summary>
    private static Sprite CreateOilSprite(int seed)
    {
        const int size = 128;
        System.Random random = new System.Random(seed);
        float a = (float)random.NextDouble() * 6.283f;
        float b = (float)random.NextDouble() * 6.283f;
        float c = (float)random.NextDouble() * 6.283f;
        const int dropletCount = 6;
        Vector2[] droplets = new Vector2[dropletCount];
        float[] dropletRadii = new float[dropletCount];
        for (int i = 0; i < dropletCount; i++)
        {
            float angle = (float)random.NextDouble() * 6.283f;
            float distance = 0.36f + (float)random.NextDouble() * 0.1f;
            droplets[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            dropletRadii[i] = 0.025f + (float)random.NextDouble() * 0.04f;
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "OilSplatTexture",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[size * size];
        Color baseColor = new Color(0.035f, 0.028f, 0.018f);
        Color gloss = new Color(0.42f, 0.36f, 0.26f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                float angle = Mathf.Atan2(p.y, p.x);
                float radius = 0.27f + 0.05f * Mathf.Sin(angle * 3f + a) + 0.035f * Mathf.Sin(angle * 5f + b)
                    + 0.02f * Mathf.Sin(angle * 9f + c);
                float edge = radius - p.magnitude;
                for (int i = 0; i < dropletCount; i++)
                    edge = Mathf.Max(edge, dropletRadii[i] - Vector2.Distance(p, droplets[i]));
                float alpha = Mathf.Clamp01(edge / 0.012f + 0.5f);

                // 左上寄りにぬめった光沢を入れます。
                Vector2 highlight = p - new Vector2(-0.08f, 0.1f);
                float shine = Mathf.Clamp01(1f - highlight.magnitude / 0.13f) * Mathf.Clamp01(edge / 0.05f);
                Color color = Color.Lerp(baseColor, gloss, shine * shine * 0.8f);
                color.a = alpha * 0.96f;
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    /// <summary>中央が透明で縁ほど濃くなる、画面縁の色付け用です。</summary>
    private static Sprite CreateEdgeSprite()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ItemEdgeTexture",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / size * 2f - 1f);
                float v = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
                float edge = Mathf.Max(u, v);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, edge));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
