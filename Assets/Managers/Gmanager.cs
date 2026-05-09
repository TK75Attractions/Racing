using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

// Game Manager
// シーン全体のゲーム進行（入力管理、車の生成、カメラ制御、状態遷移など）を行うシングルトン
public class Gmanager : MonoBehaviour
{
    // --- 機能追加の方法 ---
    // - 新しいマネージャを追加する場合:
    //   1) フィールドをクラス上部に宣言する（例: public XxxManager xxxManager;）
    //   2) Awake() 内で GetComponent<...>() か Find で取得し、必要なら初期化メソッドを呼ぶ
    //      // 例: xxxManager = GetComponent<XxxManager>(); xxxManager.Init();
    // - ゲーム開始／終了に関わる初期化は GameStart() / (将来的に) GameEnd() にまとめる
    //   -> 車やエフェクト、UI の生成・初期化は GameStart() に書く
    // - 毎フレームの処理は Update() に追加するが、状態ごとの処理は state による分岐で管理する
    //   -> 例: if (state == State.Game) { /* ゲーム中の処理 */ }
    // - デバッグ用処理は #if UNITY_EDITOR や isDebugMode フラグで囲む
    // - 新しいイベントやコールバックは専用メソッドにまとめ、Awake() で購読、OnDestroy() で解約する
    // ---------------------------------------------

    // シングルトンインスタンス: シーン内で1つだけ存在する
    public static Gmanager Control = null;
    public InputManager IManager = null;
    public CinemachineCamera VCamera;
    public CarControl car = null;
    public GameObject carPrefab;

    // コースデータ
    // レースコース情報（コース判定や経路情報を保持）
    public RaceCourse course;

    // デバッグ用
    // デバッグ用途の位置参照（エディタ上で指定）
    public Transform test;

    // 時間を格納
    // 経過時間（秒）
    public float time = 0;

    // ゲームの状態を表す列挙型
    // Title: タイトル画面
    // Game:  ゲームプレイ中
    // Result: 結果画面
    public enum State
    {
        Title,
        Game,
        Result
    }

    public State state = State.Title;


    // Unity: Awake
    // オブジェクト生成時の初期化処理を行う（シングルトン設定、各種マネージャ取得・初期化）
    public void Awake()
    {
        // シングルトンの初期化
        if (Control == null) Control = this;
        else
        {
            Destroy(this.gameObject);
            return;
        }

        // 各種マネージャ参照の取得と初期化
        IManager = GetComponent<InputManager>();
        IManager.Init();

        VCamera = transform.parent.Find("VCamera").GetComponent<CinemachineCamera>();
    }


    // Unity: Update
    // 毎フレームの更新処理（入力更新、車の更新、デバッグ処理など）
    public void Update()
    {
        float dt = Time.deltaTime;
        time += dt;

        // 入力を更新。タイトル画面でペダルが閾値を超えたらゲーム開始（暫定判定）
        if (IManager != null)
        {
            IManager.UpdateInput(dt);
            if (IManager.peddale > 1 && state == State.Title) GameStart();
        }

        // 車が存在する場合は車の更新処理を実行
        if (car != null) car.UpdateCar(dt);
        // デバッグ: スペースキー押下で test がコース内にあるかチェックしてログ出力（エディタ実行向け）
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (course != null)
            {
                Debug.Log(course.IsPointInsideCourse(new Vector2(test.position.x, test.position.z)));
            }
        }
    }

    // ゲーム開始処理。
    // ゲーム開始処理: 車を生成して初期化し、カメラ追従を設定、ゲーム状態を Game に遷移させる
    private void GameStart()
    {
        car = Instantiate(carPrefab).GetComponent<CarControl>();
        car.Init(Vector3.zero);
        VCamera.Follow = car.transform;
        state = State.Game;
        Debug.Log("Game Start");
    }
}
