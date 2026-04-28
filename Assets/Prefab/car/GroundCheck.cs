using UnityEngine;

public class GroundCheck : MonoBehaviour
{
    [SerializeField] private float checkDistance = 1.0f;
    [SerializeField] private Vector3 rayOriginOffset = Vector3.zero;

    public bool isGround;

    private void FixedUpdate()
    {
        Vector3 origin = transform.position + rayOriginOffset;
        Ray ray = new Ray(origin, Vector3.down);

        if (Physics.Raycast(ray, out RaycastHit hit, checkDistance))
        {
            isGround = hit.collider.CompareTag("Ground");
        }
        else
        {
            isGround = true; //テスト用
        }
    }
}
