using UnityEngine;

/// <summary>
/// レースの節目に稲妻を落とします。
/// 追い抜いた瞬間は抜いた車から抜かれた車へ、ファイナルラップ突入時はそのプレイヤーの前方へ空から落とします。
/// </summary>
[DisallowMultipleComponent]
public sealed class RaceLightningDirector : MonoBehaviour
{
    private const int PlayerCount = 2;

    [Header("Overtake")]
    [SerializeField] private bool overtakeLightningEnabled = true;
    [Tooltip("スタート直後の並走で稲妻が出ないよう、レース開始からこの秒数は追い抜きを演出しません。")]
    [SerializeField, Min(0f)] private float overtakeStartDelaySeconds = 3f;
    [Tooltip("抜きつ抜かれつで連発しないための間隔（秒）。")]
    [SerializeField, Min(0f)] private float overtakeCooldownSeconds = 2.5f;
    [Tooltip("2台がこの距離（m）より離れているときは演出しません。リスポーンなどによる順位の入れ替わりを除外します。")]
    [SerializeField, Min(0f)] private float overtakeMaxDistance = 30f;
    [SerializeField, Min(0f)] private float overtakeWidth = 0.22f;
    [SerializeField, Min(0.01f)] private float overtakeDuration = 0.4f;
    [SerializeField, Range(0f, 1f)] private float overtakerScreenFlash = 0.35f;
    [Tooltip("抜かれた側のカメラへ伝える揺れの強さ。")]
    [SerializeField, Range(0f, 1f)] private float overtakenCameraImpact = 0.3f;
    [SerializeField, Range(0f, 1f)] private float overtakeSoundVolume = 0.6f;

    [Header("Final Lap")]
    [SerializeField] private bool finalLapLightningEnabled = true;
    [Tooltip("車の前方、この距離（m）に落とします。")]
    [SerializeField, Min(0f)] private float finalLapStrikeAhead = 50f;
    [Tooltip("コースを塞がないよう、左右どちらかへずらす距離（m）。")]
    [SerializeField, Min(0f)] private float finalLapStrikeSideOffset = 14f;
    [Tooltip("稲妻が始まる高さ（m）。")]
    [SerializeField, Min(1f)] private float finalLapStrikeHeight = 90f;
    [SerializeField, Min(0f)] private float finalLapWidth = 1.1f;
    [SerializeField, Min(0.01f)] private float finalLapDuration = 0.55f;
    [SerializeField] private Color finalLapColor = new Color(0.7f, 0.78f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float finalLapScreenFlash = 1f;
    [SerializeField, Range(0f, 1f)] private float finalLapCameraImpact = 0.35f;
    [Tooltip("落雷の瞬間から、ゴロゴロという遠雷が届くまでの時間（秒）。")]
    [SerializeField, Min(0f)] private float finalLapRumbleDelay = 0.35f;

    private readonly Transform[] cars = new Transform[PlayerCount];
    private readonly RaceSpeedVisualController[] raceVisuals = new RaceSpeedVisualController[PlayerCount];
    private readonly bool[] finalLapAnnounced = new bool[PlayerCount];
    private VManager volumeManager;
    private int lastLeaderIndex = -1;
    private float nextOvertakeTime;

    /// <summary>画面の光と揺れを、各プレイヤーの画面へ届けるための参照を受け取ります。</summary>
    public void Configure(VManager manager, RaceSpeedVisualController[] visuals)
    {
        volumeManager = manager;
        for (int index = 0; index < PlayerCount; index++)
        {
            raceVisuals[index] = visuals != null && index < visuals.Length ? visuals[index] : null;
        }
    }

    /// <summary>新しいレースの開始時と、レースを片付けるときに呼びます。</summary>
    public void ResetRace()
    {
        for (int index = 0; index < PlayerCount; index++)
        {
            cars[index] = null;
            finalLapAnnounced[index] = false;
        }

        lastLeaderIndex = -1;
        nextOvertakeTime = 0f;
        LightningEffects.ClearAll();
    }

    public void SetCar(int playerIndex, Transform car)
    {
        if (playerIndex >= 0 && playerIndex < PlayerCount) cars[playerIndex] = car;
    }

    /// <summary>
    /// 先頭のプレイヤーを受け取り、入れ替わった瞬間に稲妻を走らせます。
    /// 先頭が決められない（片方が完走した、途中棄権したなど）ときは -1 を渡します。
    /// </summary>
    public void UpdateOvertake(float raceTime, int leaderIndex)
    {
        if (leaderIndex < 0 || leaderIndex >= PlayerCount) return;
        int previousLeader = lastLeaderIndex;
        lastLeaderIndex = leaderIndex;
        if (!overtakeLightningEnabled || previousLeader < 0 || previousLeader == leaderIndex) return;
        if (raceTime < overtakeStartDelaySeconds || raceTime < nextOvertakeTime) return;

        Transform overtaker = cars[leaderIndex];
        Transform overtaken = cars[previousLeader];
        if (overtaker == null || overtaken == null) return;
        if (Vector3.Distance(overtaker.position, overtaken.position) > overtakeMaxDistance) return;

        nextOvertakeTime = raceTime + overtakeCooldownSeconds;
        PlayOvertake(leaderIndex, overtaker, previousLeader, overtaken);
    }

    /// <summary>周回数を受け取り、ファイナルラップに入った最初の1回だけ落雷させます。</summary>
    public void UpdateLap(int playerIndex, int completedLaps, int goalLap)
    {
        if (playerIndex < 0 || playerIndex >= PlayerCount || finalLapAnnounced[playerIndex]) return;
        // 1周のレースはスタートからファイナルラップなので演出しません。
        if (goalLap <= 1 || completedLaps < goalLap - 1) return;

        finalLapAnnounced[playerIndex] = true;
        if (!finalLapLightningEnabled || completedLaps >= goalLap || cars[playerIndex] == null) return;
        PlayFinalLap(playerIndex, cars[playerIndex]);
    }

    private void PlayOvertake(int overtakerIndex, Transform overtaker, int overtakenIndex, Transform overtaken)
    {
        Color color = Color.Lerp(PlayerCarPaint.GetPlayerColor(overtakerIndex), Color.white, 0.35f);
        LightningStyle style = new LightningStyle
        {
            color = color,
            width = overtakeWidth,
            jaggedness = 0.3f,
            subdivisions = 5,
            branchCount = 3,
            duration = overtakeDuration,
            regenerateInterval = 0.05f,
            lightIntensity = 20f,
            lightRange = 18f,
            lightPlacement = 0.5f
        };

        // 両端を車に追従させ、高速で走っていても2台をつなぎ続けます。
        LightningEffects.Strike(
            new LightningAnchor(overtaker, new Vector3(0f, 1f, 0.5f)),
            new LightningAnchor(overtaken, new Vector3(0f, 0.8f, 0f)),
            style);

        // 抜かれた車の周りに、撃たれたような小さな放電を散らします。
        LightningStyle hit = new LightningStyle
        {
            color = color,
            width = 0.07f,
            jaggedness = 0.35f,
            subdivisions = 3,
            duration = overtakeDuration * 0.6f,
            regenerateInterval = 0.03f
        };
        for (int index = 0; index < 4; index++)
        {
            Vector3 start = new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(0.4f, 1.2f), Random.Range(-2f, 2f));
            Vector3 end = start + Random.onUnitSphere * Random.Range(0.8f, 1.6f);
            LightningEffects.Strike(new LightningAnchor(overtaken, start), new LightningAnchor(overtaken, end), hit);
        }

        if (volumeManager != null) volumeManager.FlashLightning(overtakerIndex, overtakerScreenFlash, color);
        RaceSpeedVisualController overtakenVisuals = raceVisuals[overtakenIndex];
        if (overtakenVisuals != null && overtakenCameraImpact > 0f) overtakenVisuals.AddImpact(overtakenCameraImpact);
        LightningEffects.PlayCrack(overtakeSoundVolume);
    }

    private void PlayFinalLap(int playerIndex, Transform car)
    {
        Vector3 forward = Vector3.ProjectOnPlane(car.forward, Vector3.up);
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector3 target = car.position + forward * finalLapStrikeAhead + right * (finalLapStrikeSideOffset * side);
        Vector3 ground = FindGround(target, car.position.y);
        Vector3 sky = ground + Vector3.up * finalLapStrikeHeight +
            new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));

        LightningEffects.Strike(
            new LightningAnchor(sky),
            new LightningAnchor(ground),
            new LightningStyle
            {
                color = finalLapColor,
                width = finalLapWidth,
                jaggedness = 0.28f,
                subdivisions = 6,
                branchCount = 5,
                duration = finalLapDuration,
                regenerateInterval = 0.07f,
                lightIntensity = 400f,
                lightRange = 60f,
                lightPlacement = 0.93f
            });

        // 着弾点から地面を這う放電を放射状に広げます。
        LightningStyle groundArc = new LightningStyle
        {
            color = finalLapColor,
            width = 0.25f,
            jaggedness = 0.35f,
            subdivisions = 4,
            branchCount = 1,
            duration = finalLapDuration * 0.6f,
            regenerateInterval = 0.04f
        };
        for (int index = 0; index < 5; index++)
        {
            float angle = (index / 5f + Random.Range(-0.06f, 0.06f)) * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 end = ground + direction * Random.Range(3f, 6f) + Vector3.up * 0.15f;
            LightningEffects.Strike(new LightningAnchor(ground + Vector3.up * 0.3f), new LightningAnchor(end), groundArc);
        }

        if (volumeManager != null) volumeManager.FlashLightning(playerIndex, finalLapScreenFlash, finalLapColor);
        RaceSpeedVisualController visuals = raceVisuals[playerIndex];
        if (visuals != null && finalLapCameraImpact > 0f) visuals.AddImpact(finalLapCameraImpact);
        LightningEffects.PlayThunder(1f, 1f, finalLapRumbleDelay);
    }

    private static Vector3 FindGround(Vector3 point, float fallbackHeight)
    {
        Vector3 origin = new Vector3(point.x, fallbackHeight + 150f, point.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        return new Vector3(point.x, fallbackHeight, point.z);
    }
}
