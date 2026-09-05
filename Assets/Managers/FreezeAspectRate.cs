using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * FreezeAspectRate
 * - 画面比率を `aspect` に固定し、余白部分をスプライトで埋めるカメラ管理スクリプトです。
 * - `aspect` は、画面の幅と高さの比率を表す Vector2Int で、デフォルトは 16:9 に設定されています。
 * - `colorbase` は、余白部分のスプライトの色を指定する Color32 で、デフォルトは黒色に設定されています。
 * - これはモニターの画面比率が異なる場合に、ゲームの表示が崩れないようにするためのスクリプトです。(あんまりいじらなくていいよ)
 */


[ExecuteInEditMode]
public class FreezeAspectRate : MonoBehaviour
{
    public Vector2Int aspect = new Vector2Int(16,9);
    public Color32 colorbase = Color.black;
    [SerializeField] private float aspectRate;
    [SerializeField] private float cameraSize = 0;
    [SerializeField] private float oldSize = 0;
    [SerializeField] private float setTime = 0;
    [SerializeField] private Camera main;
    [SerializeField] private Camera backCamera;
    [SerializeField] private Camera UICamera;
    [SerializeField] private Camera frontCamera;
    [SerializeField] private Camera backImageCamera;
    [SerializeField] private int displayIndex;
    [SerializeField] private Sprite Sup;
    [SerializeField] private Sprite Sdown;
    [SerializeField] private Sprite Sright;
    [SerializeField] private Sprite Sleft;
    [SerializeField] private Transform up, down, right, left;
    private Rect currentRect = new Rect(0, 0, 1, 1);
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Vector2Int lastAspect;
    private Color32 lastColorbase;

    public void Awake()
    {
        ResolveLocalCameras();

        CreateBackCamera();
        UpdateScreenRate();
    }

    public void ConfigureForDisplay(int targetDisplay, Camera displayBackCamera)
    {
        displayIndex = Mathf.Max(0, targetDisplay);
        backCamera = displayBackCamera;
        ResolveLocalCameras();
        SetCameraTargetDisplay(main);
        SetCameraTargetDisplay(UICamera);
        SetCameraTargetDisplay(frontCamera);
        SetCameraTargetDisplay(backImageCamera);
        SetCameraTargetDisplay(backCamera);
        CreateBackCamera();
        UpdateScreenRate();
    }

    private void Update()
    {
        ChangeSize();

        if (IsChangeAspect()) return;
        UpdateScreenRate();
        main.ResetAspect();
    }

    private void CreateBackCamera()
    {
#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlaying) return;

#endif
        Debug.Log("Set BackCamera");
        ConfigureBackCamera();
        SetLetterboxSpritesEnabled(false);
    }

    private void ConfigureBackCamera()
    {
        if (backCamera == null) return;

        backCamera.transform.position = new Vector3(0, 0, -5);
        backCamera.rect = new Rect(0, 0, 1, 1);
        backCamera.depth = -99;
        backCamera.orthographic = true;
        backCamera.clearFlags = CameraClearFlags.SolidColor;
        backCamera.backgroundColor = colorbase;
        backCamera.cullingMask = 0;
        backCamera.farClipPlane = 10;
        backCamera.nearClipPlane = 1;
        backCamera.depthTextureMode = DepthTextureMode.None;
        backCamera.renderingPath = RenderingPath.VertexLit;
        backCamera.useOcclusionCulling = false;
    }

    private void UpdateScreenRate()
    {
        if (aspect.x <= 0 || aspect.y <= 0) return;
        GetDisplaySize(out int displayWidth, out int displayHeight);
        if (displayWidth <= 0 || displayHeight <= 0) return;
        if (main == null || UICamera == null || frontCamera == null || backImageCamera == null) return;

        aspectRate = (float)aspect.x / aspect.y;
        float baseAspect = (float)aspect.y / aspect.x;
        float nowAspect = (float)displayHeight / displayWidth;

        if (float.IsNaN(baseAspect) || float.IsInfinity(baseAspect)) return;
        if (float.IsNaN(nowAspect) || float.IsInfinity(nowAspect)) return;
        
        if (baseAspect > nowAspect)
        {
            float change = nowAspect / baseAspect;
            Rect set = new Rect((1 - change) * 0.5f, 0, change, 1);
            ApplyCameraRect(set);
        }
        else
        {
            float change = baseAspect / nowAspect;
            Rect set = new Rect(0, (1 - change) * 0.5f, 1, change);
            ApplyCameraRect(set);
        }
    }

    private void ApplyCameraRect(Rect set)
    {
        currentRect = set;
        main.rect = set;
        UICamera.rect = set;
        frontCamera.rect = set;
        backImageCamera.rect = set;

        if (backCamera != null)
        {
            ConfigureBackCamera();
        }

        SetLetterboxSpritesEnabled(false);
        GetDisplaySize(out lastScreenWidth, out lastScreenHeight);
        lastAspect = aspect;
        lastColorbase = colorbase;
    }

    private bool IsChangeAspect()
    {
        GetDisplaySize(out int displayWidth, out int displayHeight);
        return displayWidth == lastScreenWidth
            && displayHeight == lastScreenHeight
            && lastAspect == aspect
            && lastColorbase.Equals(colorbase)
            && Mathf.Approximately(currentRect.width / currentRect.height, aspectRate);
    }

    private void ChangeSize()
    {
        if (cameraSize == oldSize) return;

        setTime += Time.deltaTime;
        float f;
        if (setTime < 0.4f) f = oldSize + (cameraSize - oldSize) * setTime / 0.4f;
        else
        {
            f = cameraSize;
            oldSize = cameraSize;
        }
        
        SetOrthographicSize(main, f);
        SetOrthographicSize(UICamera, f);
        SetOrthographicSize(frontCamera, f);
        SetOrthographicSize(backCamera, f);
    }

    public void SetCameraSize(float f)
    {
        if (f > 0)
        {
            oldSize = cameraSize;
            cameraSize = f;
            setTime = 0;
        }
    }

    public void SetCameraSizeImediately(float f)
    {
        if(f > 0)
        {
            cameraSize = f;
            oldSize = f;
            SetOrthographicSize(main, f);
            SetOrthographicSize(UICamera, f);
            SetOrthographicSize(frontCamera, f);
            SetOrthographicSize(backCamera, f);
        }
    }

    private void SetOrthographicSize(Camera target, float size)
    {
        if (target != null && target.orthographic)
        {
            target.orthographicSize = size;
        }
    }

    private void SetLetterboxSpritesEnabled(bool enabled)
    {
        SetSpriteEnabled(up, enabled);
        SetSpriteEnabled(down, enabled);
        SetSpriteEnabled(right, enabled);
        SetSpriteEnabled(left, enabled);
    }

    private void SetSpriteEnabled(Transform target, bool enabled)
    {
        if (target == null) return;
        SpriteRenderer spriteRenderer = target.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        spriteRenderer.enabled = enabled;
    }

    public Camera GetUICamera() => UICamera;

    private void ResolveLocalCameras()
    {
        aspectRate = aspect.y != 0 ? (float)aspect.x / aspect.y : 1f;
        main = GetComponent<Camera>();

        Transform cameraRoot = transform.parent;
        if (cameraRoot != null)
        {
            UICamera = GetCamera(cameraRoot.Find("UICamera"));
            frontCamera = GetCamera(cameraRoot.Find("FrontCamera"));
            backImageCamera = GetCamera(cameraRoot.Find("BackImageCamera"));
        }

        if (backCamera == null && cameraRoot != null && cameraRoot.parent != null)
        {
            string backCameraName = displayIndex == 0 ? "BackCamera" : $"BackCamera_P{displayIndex + 1}";
            backCamera = GetCamera(cameraRoot.parent.Find(backCameraName));
        }
    }

    private void GetDisplaySize(out int width, out int height)
    {
        if (Application.isPlaying && displayIndex >= 0 && displayIndex < Display.displays.Length)
        {
            Display display = Display.displays[displayIndex];
            width = display.renderingWidth;
            height = display.renderingHeight;
            if (width > 0 && height > 0)
            {
                return;
            }

            width = display.systemWidth;
            height = display.systemHeight;
            if (width > 0 && height > 0)
            {
                return;
            }
        }

        width = Screen.width;
        height = Screen.height;
    }

    private void SetCameraTargetDisplay(Camera target)
    {
        if (target != null)
        {
            target.targetDisplay = displayIndex;
        }
    }

    private static Camera GetCamera(Transform target)
    {
        return target != null ? target.GetComponent<Camera>() : null;
    }
}
