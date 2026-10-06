using UnityEngine;

/// <summary>壁への衝突時に、衝突した速さと同じ速さで衝突面の法線方向へ跳ね返します。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarWallCollisionResponse : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    [Tooltip("この値以下の接触法線を壁として扱います。")]
    private float maximumWallNormalY = 0.65f;

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

        // 物理エンジンが衝突を解決した後の body.linearVelocity ではなく、衝突時の速さを使います。
        float impactSpeed = collision.relativeVelocity.magnitude;
        // 力を追加せず速度を直接設定し、車体の質量によらず同じ速さで壁から離します。
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
            if (Mathf.Abs(contactNormal.y) <= maximumWallNormalY)
            {
                wallNormal += contactNormal;
            }
        }

        return wallNormal.sqrMagnitude > 0.0001f;
    }
}
