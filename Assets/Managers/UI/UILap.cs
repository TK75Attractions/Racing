using UnityEngine;
using UnityEngine.UI;

public class UILap : MonoBehaviour
{
    private CanvasGroup CG;
    private RectTransform lapRect;
    private Image lapImage;

    private int currentLap = 0;
    private float timer;
    private const float animationDuration = 0.5f;

    public void Init()
    {
        CG = GetComponent<CanvasGroup>();
        lapRect = transform.Find("Number").GetComponent<RectTransform>();
        lapImage = lapRect.GetComponent<Image>();
    }

    public void UpdateLap(int lap, float deltaTime)
    {
        if (lap != currentLap)
        {
            currentLap = lap;
            SetLap(lap);
            timer = 0f;
        }

        if (currentLap > 0)
        {
            timer += deltaTime;
            float scale = 1f + Mathf.Sin(timer * 5f) * 0.1f;
            lapRect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void SetLap(int lap)
    {
        if (lap < 1 || lap > 99)
        {
            CG.alpha = 0f;
            return;
        }

        if (timer > animationDuration) CG.alpha = 1f;
        else CG.alpha = timer / animationDuration;

        // Assuming you have sprites for laps 1 to 99 in numberSprites array
        lapImage.sprite = Gmanager.Control.NumberSprites[lap - 1];
    }

}