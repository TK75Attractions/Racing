using UnityEngine;

public class PlayerControl : MonoBehaviour
{
    private Rigidbody rb;
    public float acceleration = 3f;
    public float maxSpeed = 10f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void UpdateControl(float dt)
    {
        Move(dt);



    }

    private void Move(float dt)
    {
        int z = 0;
        if (GManager.Control.IManager.UpPressed) z = 1;
        else if (GManager.Control.IManager.DownPressed) z = -1;

        int x = 0;
        if (GManager.Control.IManager.LeftPressed) x = -1;
        else if (GManager.Control.IManager.RightPressed) x = 1;

        Vector2 dv = new Vector2(x, z).normalized * acceleration;
        rb.AddForce(new Vector3(dv.x, 0, dv.y), ForceMode.VelocityChange);
        Vector2 vel = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);

        if (vel.magnitude > maxSpeed)
        {
            vel = vel.normalized * maxSpeed;
            rb.linearVelocity = new Vector3(vel.x, rb.linearVelocity.y, vel.y);
        }
    }
}
