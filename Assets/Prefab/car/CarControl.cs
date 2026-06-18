using System;
using System.Collections.Generic;
using UnityEngine;

public class CarControl : MonoBehaviour
{
    private Transform trans;
    private Rigidbody rb;

    private GroundCheck groundCheck;
    private GroundCheck[] tireGroundChecks = Array.Empty<GroundCheck>();

    [SerializeField] private int tireType = 0;
    [SerializeField] private List<GameObject> tirePrefabs = new List<GameObject>();

    public void Init(Vector3 position)
    {
        trans = transform;
        rb = GetComponent<Rigidbody>();

        Transform groundCheckTransform = trans.Find("GroundCheck");
        if (groundCheckTransform != null)
        {
            groundCheck = groundCheckTransform.GetComponent<GroundCheck>();
        }

        TireForce[] tires = GetComponentsInChildren<TireForce>();
        tireGroundChecks = new GroundCheck[tires.Length];

        for (int index = 0; index < tires.Length; index++)
        {
            tireGroundChecks[index] = GetOrAddGroundCheck(tires[index].transform);
        }
    }

    public void UpdateCar(float dt)
    {
        Vector2 input = new Vector2(Mathf.Sin(Gmanager.Control.IManager.handle), Mathf.Cos(Gmanager.Control.IManager.handle)) * Gmanager.Control.IManager.peddale;
        UpdateSimulateTarget(input, dt);
    }

    private void UpdateSimulateTarget(Vector2 input, float dt)
    {
        if (!HasGroundedTire())
        {
            return;
        }

        Vector3 inputVector = new Vector3(input.x, 0, input.y);
        rb.AddForce(inputVector * dt * 100, ForceMode.Acceleration);
    }

    private GroundCheck GetOrAddGroundCheck(Transform tire)
    {
        GroundCheck tireGroundCheck = tire.GetComponent<GroundCheck>();
        if (tireGroundCheck == null)
        {
            tireGroundCheck = tire.gameObject.AddComponent<GroundCheck>();
        }

        return tireGroundCheck;
    }

    private bool HasGroundedTire()
    {
        bool hasTireGroundCheck = false;

        foreach (GroundCheck tireGroundCheck in tireGroundChecks)
        {
            if (tireGroundCheck == null)
            {
                continue;
            }

            hasTireGroundCheck = true;
            if (tireGroundCheck.CheckNow())
            {
                return true;
            }
        }

        return !hasTireGroundCheck && groundCheck != null && groundCheck.CheckNow();
    }
}
