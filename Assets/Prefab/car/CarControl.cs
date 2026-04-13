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
    private TireData[] tireDatas = new TireData[4];

    [Serializable]
    private struct TireData
    {
        public GameObject tireObject;
        public Vector3 position;
        public Vector3 scale;
    }

    public void Init(Vector3 position)
    {
        trans = transform;
        tireParent = trans.Find("Tires");
        rbSimulate = GetComponent<Rigidbody>();
        groundCheck = trans.Find("GroundCheck").GetComponent<GroundCheck>();

        foreach (Transform t in tireParent) Destroy(t.gameObject);
        simulateTarget = Instantiate(simulateTargetPrefab).transform;
        simulateTarget.position = position;



    }

    public void UpdateCar()
    {

    }

    private void UpdateSimulateTarget(Vector2Int input, float dt)
    {
        if (groundCheck.isGround == false) return;
        Vector3 inputVector = new Vector3(input.x, 0, input.y);
        rbSimulate.AddForce(inputVector * dt);
    }


}
