using UnityEngine;

/// <summary>壁への衝突時に、衝突した速さと同じ速さで衝突面の法線方向へ跳ね返します。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarWallCollisionResponse : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    [Tooltip("接触法線Yの絶対値がこの値未満の面を壁として扱います。接地判定の下限（0.2）を超えない値にしてください。")]
    private float maximumWallNormalY = 0.2f;

    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!TryGetWallNormal(collision, out Vector3 wallNormal))
        {
            return;
        }

        // 物理エンジンが衝突を解決した後の速度ではなく、衝突時の速さを使います。
        float impactSpeed = collision.relativeVelocity.magnitude;
        body.linearVelocity = wallNormal.normalized * impactSpeed;
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
