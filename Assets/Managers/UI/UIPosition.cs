using UnityEngine;
using UnityEngine.UI;

public class UIPosition : MonoBehaviour
{
    private CanvasGroup CG;
    private RectTransform posRect;
    private Image posImage;

    private int currentPosition = 0;
    private float timer;
    private const float animationDuration = 0.5f;

    public void Init()
    {
        CG = GetComponent<CanvasGroup>();
        posRect = transform.Find("Number").GetComponent<RectTransform>();
        posImage = posRect.GetComponent<Image>();
    }

    public void UpdatePosition(int position, float deltaTime)
    {
        if (position != currentPosition)
        {
            currentPosition = position;
            SetPosition(position);
            timer = 0f;
        }

        if (currentPosition > 0)
        {
            timer += deltaTime;
            float scale = 1f + Mathf.Sin(timer * 5f) * 0.1f;
            posRect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    public void SetPosition(int position)
    {
        if (position < 1 || position > Gmanager.Control.NumberSprites.Length)
        {
            CG.alpha = 0f;
            return;
        }

        if (timer > animationDuration) CG.alpha = 1f;
        else CG.alpha = timer / animationDuration;

        posImage.sprite = Gmanager.Control.NumberSprites[position - 1];
    }
}
