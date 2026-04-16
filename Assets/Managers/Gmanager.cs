using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;

public class Gmanager : MonoBehaviour
{
    public Gmanager Control = null;
    public InputManager IManager = null;
    public CarControl car = null;
    public GameObject carPrefab;

    public float time = 0;

    public enum State
    {
        Title,
        Game,
        Result
    }

    public State state = State.Title;


    public void Awake()
    {
        if (Control == null) Control = this;
        else
        {
            Destroy(this.gameObject);
            return;
        }

        IManager = GetComponent<InputManager>();
        IManager.Init();
    }


    public void Update()
    {
        float dt = Time.deltaTime;
        time += dt;

        if (IManager != null)
        {
            IManager.UpdateInput(dt);
            if (IManager.peddale > 1 && state == State.Title) GameStart();
        }

        if (car != null) car.UpdateCar(dt);
    }

    private void GameStart()
    {
        car = Instantiate(carPrefab).GetComponent<CarControl>();
        car.Init(Vector3.zero);
        state = State.Game;
        Debug.Log("Game Start");
    }
}
