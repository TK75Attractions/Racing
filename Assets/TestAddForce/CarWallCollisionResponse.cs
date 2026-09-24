using UnityEngine;

/// <summary>壁への衝突時に車を停止させ、衝突面の法線方向へ一定の反発を与えます。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarWallCollisionResponse : MonoBehaviour
{
    [SerializeField, Min(0f)]
    [Tooltip("壁に当たったときに衝突面の法線方向へ加える反発の大きさです。")]
    private float wallBounceImpulse = 8f;

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

        // 衝突前の速さには依存させず、まず現在の並進速度を必ず消します。
        body.linearVelocity = Vector3.zero;

        if (wallNormal.sqrMagnitude > 0.0001f)
        {
            // 車体の向きではなく、実際にぶつかった面の法線方向へ押し出します。
            body.AddForce(wallNormal.normalized * wallBounceImpulse, ForceMode.Impulse);
        }
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
