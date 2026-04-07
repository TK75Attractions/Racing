using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GManager : MonoBehaviour
{
    static public GManager Control;

    public InputManager IManager;
    public GameObject CarPrefab;
    public PlayerControl PC;

    [SerializeField] private float time = 0;

    public void Awake()
    {
        if (Control == null)
        {
            Control = this;
            DontDestroyOnLoad(this.transform.parent.gameObject);
            Application.targetFrameRate = 30;
        }
        else
        {
            Destroy(this.gameObject);
            return;
        }

        IManager = GetComponent<InputManager>();

        GameObject car = Instantiate(CarPrefab, Vector3.zero, Quaternion.identity);
        PC = car.GetComponent<PlayerControl>();

    }

    public void Update()
    {
        float dt = Time.deltaTime;
        time += dt;

        //InputManager の UpdateInput 関数を呼び出して、入力を検出します。
        if (IManager != null)
        {
            IManager.UpdateInput();
        }
        if (PC != null)
        {
            PC.UpdateControl(dt);
        }
    }
}

