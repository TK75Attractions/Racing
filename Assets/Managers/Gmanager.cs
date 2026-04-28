using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class Gmanager : MonoBehaviour
{
    public static Gmanager Control = null;
    [SerializeField] public InputManager IManager = null;
    public CinemachineCamera VCamera;
    public CarControl car = null;
    public GameObject carPrefab;

    public RaceCourse course;

    public Transform test;

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

        VCamera = transform.parent.Find("VCamera").GetComponent<CinemachineCamera>();
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
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (course != null)
            {
                Debug.Log(course.IsPointInsideCourse(new Vector2(test.position.x, test.position.z)));
            }
        }
    }

    private void GameStart()
    {
        car = Instantiate(carPrefab).GetComponent<CarControl>();
        car.Init(Vector3.zero);
        VCamera.Follow = car.transform;
        state = State.Game;
        Debug.Log("Game Start");
    }
}
