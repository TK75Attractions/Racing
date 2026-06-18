using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
public class DebugMover : MonoBehaviour
{
    [SerializeField] private float forceMultiplier = 300f;
    [SerializeField] private float torqueMultiplier = 15f;

    [SerializeField] private List<TireForce> tires = new List<TireForce>();

    private Rigidbody rb;
    [SerializeField] private float h;
    [SerializeField] private float p;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();

        TireForce[] foundTires = GetComponentsInChildren<TireForce>();
        tires.Clear();
        tires.AddRange(foundTires);
        foreach (var tire in tires)
        {
            tire.Init(rb);
        }
        if (rb == null)
        {
            Debug.LogError($"{gameObject.name} に Rigidbody が付いていません! AddForceできません。");
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (rb == null) return;
        h = Gmanager.Control.IManager.handle;
        p = Gmanager.Control.IManager.peddale;

        foreach (var tire in tires)
        {
            tire.ApplyPhysics(h, p, forceMultiplier, torqueMultiplier);
        }



    }
}
