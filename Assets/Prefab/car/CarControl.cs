using System;
using System.Collections.Generic;
using UnityEngine;

public class CarControl : MonoBehaviour
{

    private Transform trans;
    private Transform tireParent;
    [SerializeField] private GameObject simulateTargetPrefab;
    private Transform simulateTarget;
    private Rigidbody rbSimulate;

    private GroundCheck groundCheck;
    [SerializeField] private int tireType = 0;

    [SerializeField] private List<GameObject> tirePrefabs = new List<GameObject>();
    private Transform[] tireDatas = new Transform[4];

    public void Init(Vector3 position)
    {
        trans = transform;
        tireParent = trans.Find("Tires");
        rbSimulate = GetComponent<Rigidbody>();
        groundCheck = trans.Find("GroundCheck").GetComponent<GroundCheck>();

        foreach (Transform t in tireParent) Destroy(t.gameObject);
        simulateTarget = Instantiate(simulateTargetPrefab).transform;
        simulateTarget.position = position;

        tireDatas[0] = Instantiate(tirePrefabs[tireType], tireParent).transform;
        tireDatas[0].localPosition = new Vector3(0.8f, 0.3f, 1.25f);
        tireDatas[0].localScale = new Vector3(1, 1, 1);

        tireDatas[1] = Instantiate(tirePrefabs[tireType], tireParent).transform;
        tireDatas[1].localPosition = new Vector3(-0.8f, 0.3f, 1.25f);
        tireDatas[1].localScale = new Vector3(-1, 1, 1);

        tireDatas[2] = Instantiate(tirePrefabs[tireType], tireParent).transform;
        tireDatas[2].localPosition = new Vector3(0.8f, 0.3f, -1.4f);
        tireDatas[2].localScale = new Vector3(1, 1, 1);

        tireDatas[3] = Instantiate(tirePrefabs[tireType], tireParent).transform;
        tireDatas[3].localPosition = new Vector3(-0.8f, 0.3f, -1.4f);
        tireDatas[3].localScale = new Vector3(-1, 1, 1);
    }

    public void UpdateCar(float dt)
    {
        Vector2 input = new Vector2(Mathf.Cos(Gmanager.Control.IManager.handle), Mathf.Sin(Gmanager.Control.IManager.handle)) * Gmanager.Control.IManager.peddale;
        UpdateSimulateTarget(input, dt);
        UpdateTires(Gmanager.Control.IManager.handle);
    }

    private void UpdateSimulateTarget(Vector2 input, float dt)
    {
        if (groundCheck.isGround == false) return;
        Vector3 inputVector = new Vector3(input.x, 0, input.y);
        rbSimulate.AddForce(inputVector * dt);
    }

    private void UpdateTires(float handle)
    {
        tireDatas[0].localRotation = Quaternion.Euler(0, handle, 0);
        tireDatas[1].localRotation = Quaternion.Euler(0, handle, 0);
    }
}
