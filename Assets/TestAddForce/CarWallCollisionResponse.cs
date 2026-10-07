using UnityEngine;

/// <summary>壁へ向かう速度だけを反射し、滑走と持続する侵入からの復帰を保ちます。</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarWallCollisionResponse : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    [Tooltip("接触法線Yの絶対値がこの値未満の面を壁として扱います。接地判定の下限（0.2）を超えない値にしてください。")]
    private float maximumWallNormalY = 0.2f;

    [Header("Stuck Recovery")]
    [SerializeField, Min(0f)] private float stuckRecoveryDelay = 0.15f;
    [SerializeField, Min(0f)] private float maximumRecoverySpeed = 10f;
    private Rigidbody body;
    private BoxCollider chassis;
    private readonly Collider[] overlapBuffer = new Collider[32];
    private float penetrationTime;
    private bool recoveryActive;
    private bool limitedDepenetration;
    private float savedDepenetrationVelocity;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        StaticWallColliderVolume.EnsureForScene(gameObject.scene);
        chassis = GetComponent<BoxCollider>();
    }

    private void FixedUpdate()
    {
        RestoreDepenetrationLimit();
        // 通常の衝突は物理エンジンに任せ、低速で侵入が残るときだけ補助します。
        if (body.isKinematic || chassis == null || !chassis.enabled || chassis.isTrigger ||
            body.linearVelocity.sqrMagnitude > 4f)
        {
            penetrationTime = 0f;
            recoveryActive = false;
            return;
        }
        Bounds bounds = chassis.bounds;
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, overlapBuffer,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        Vector3 escape = Vector3.zero;
        float escapeDistance = 0f;
        bool severeDownwardPenetration = false;
        for (int index = 0; index < count; index++)
        {
            Collider obstacle = overlapBuffer[index];
            if (obstacle == null || obstacle.attachedRigidbody == body ||
                (obstacle.attachedRigidbody != null && !obstacle.attachedRigidbody.isKinematic) ||
                obstacle.GetComponentInParent<RaceBarrierPath>() != null || obstacle.name == "WallCollider" ||
                Physics.GetIgnoreLayerCollision(gameObject.layer, obstacle.gameObject.layer) ||
                Physics.GetIgnoreCollision(chassis, obstacle)) continue;
            if (!Physics.ComputePenetration(chassis, chassis.transform.position, chassis.transform.rotation,
                obstacle, obstacle.transform.position, obstacle.transform.rotation, out Vector3 direction, out float distance) ||
                distance < 0.05f) continue;

            bool pushesThroughFloor = direction.y < -0.2f && distance > 0.3f;
            if (obstacle is BoxCollider box)
            {
                // 壁の下端へ押し出されて床と挟まれないよう、水平面で箱の分離距離を求めます。
                if (box.bounds.size.y < chassis.size.y * Mathf.Abs(chassis.transform.lossyScale.y) * 1.1f ||
                    box.bounds.max.y <= body.worldCenterOfMass.y + 0.1f ||
                    !TryGetHorizontalBoxEscape(box, out direction, out distance) ||
                    distance > bounds.size.magnitude) continue;
            }
            else if (Mathf.Abs(direction.y) >= 0.2f) continue;
            severeDownwardPenetration |= pushesThroughFloor;
            if (distance <= escapeDistance) continue;
            escape = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            escapeDistance = distance;
        }
        if (escapeDistance <= 0f) { penetrationTime = 0f; recoveryActive = false; return; }
        penetrationTime += Time.fixedDeltaTime;
        if (!recoveryActive && !severeDownwardPenetration && penetrationTime < stuckRecoveryDelay) return;
        recoveryActive = true;
        // 壁の下端の解で床を突き抜けないよう、水平脱出中だけ物理側の侵入解消速度を抑えます。
        savedDepenetrationVelocity = body.maxDepenetrationVelocity;
        body.maxDepenetrationVelocity = Mathf.Min(savedDepenetrationVelocity, 0.5f);
        limitedDepenetration = true;

        // 大きなワープや上向きの打ち上げを避け、接触解消の距離だけを小刻みに移動します。
        float step = Mathf.Min(escapeDistance + 0.015f, maximumRecoverySpeed * Time.fixedDeltaTime);
        body.position += escape * step;
        float inwardSpeed = Vector3.Dot(body.linearVelocity, escape);
        if (inwardSpeed < 0f) body.linearVelocity -= escape * inwardSpeed;
    }

    private void RestoreDepenetrationLimit()
    {
        if (!limitedDepenetration || body == null) return;
        body.maxDepenetrationVelocity = savedDepenetrationVelocity;
        limitedDepenetration = false;
    }

    private void OnDisable()
    {
        RestoreDepenetrationLimit();
        recoveryActive = false;
        penetrationTime = 0f;
    }

    private bool TryGetHorizontalBoxEscape(BoxCollider obstacle, out Vector3 direction, out float distance)
    {
        direction = Vector3.zero;
        distance = float.MaxValue;
        Vector3 obstacleCenter = obstacle.transform.TransformPoint(obstacle.center);
        Vector3 ownCenter = chassis.transform.TransformPoint(chassis.center);
        Vector3 ownScale = chassis.transform.lossyScale;
        Vector3 obstacleScale = obstacle.transform.lossyScale;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 localAxis = Vector3.zero;
            localAxis[axis] = 1f;
            Vector3 normal = obstacle.transform.TransformDirection(localAxis).normalized;
            if (Mathf.Abs(normal.y) >= 0.2f) continue;
            float ownRadius = 0f;
            for (int ownAxis = 0; ownAxis < 3; ownAxis++)
            {
                Vector3 ownLocalAxis = Vector3.zero;
                ownLocalAxis[ownAxis] = 1f;
                ownRadius += chassis.size[ownAxis] * Mathf.Abs(ownScale[ownAxis]) * 0.5f *
                    Mathf.Abs(Vector3.Dot(chassis.transform.TransformDirection(ownLocalAxis), normal));
            }
            float offset = Vector3.Dot(ownCenter - obstacleCenter, normal);
            Vector3 horizontal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
            float overlap = (ownRadius + obstacle.size[axis] * Mathf.Abs(obstacleScale[axis]) * 0.5f - Mathf.Abs(offset)) /
                Mathf.Abs(Vector3.Dot(horizontal, normal));
            if (overlap <= 0f || overlap >= distance) continue;
            direction = horizontal * (offset >= 0f ? 1f : -1f);
            distance = overlap;
        }
        return direction.sqrMagnitude > 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!TryGetWallNormal(collision, out Vector3 wallNormal))
        {
            return;
        }

        // 壁へ向かう速度成分だけを反射します。横滑り・落下速度は維持します。
        // 既に外へ離れている場合、別のタイヤから同じ衝突通知が来ても反発を重ねません。
        wallNormal = Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized;
        float incomingSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, wallNormal));
        float outwardSpeed = Vector3.Dot(body.linearVelocity, wallNormal);
        if (outwardSpeed < incomingSpeed)
            body.linearVelocity += wallNormal * (incomingSpeed - outwardSpeed);
    }

    private bool TryGetWallNormal(Collision collision, out Vector3 wallNormal)
    {
        wallNormal = Vector3.zero;
        if (collision == null || collision.contactCount == 0 ||
            (collision.rigidbody != null && !collision.rigidbody.isKinematic))
        {
            // 動的 Rigidbody（もう一台の車など）との接触は壁扱いしません。
            return false;
        }

        // レースバリアは曲面に沿って駆け上がれるため、反発の対象から除外します。
        if (collision.collider.GetComponentInParent<RaceBarrierPath>() != null ||
            collision.collider.name == "WallCollider")
        {
            return false;
        }

        // 建物などの静的コライダーは、地面と区別できる接触法線で壁判定します。
        for (int index = 0; index < collision.contactCount; index++)
        {
            Vector3 contactNormal = collision.GetContact(index).normal;
            // 坂や路面の縁では法線が斜めを向くため、接地できる面を壁判定に含めません。
            if (Mathf.Abs(contactNormal.y) < Mathf.Min(maximumWallNormalY, 0.2f))
            {
                wallNormal += contactNormal;
            }
        }

        return wallNormal.sqrMagnitude > 0.0001f;
    }
}
