using UnityEngine;

[DisallowMultipleComponent]
public sealed class ResultMenuAnimator : MonoBehaviour
{
    private PedalButtonFeedback[] feedback;
    private static readonly Color Accent = new Color(0.04f, 0.84f, 1f, 1f);

    public void Configure(RectTransform[] cards)
    {
        feedback = new PedalButtonFeedback[cards.Length];
        for (int index = 0; index < cards.Length; index++)
        {
            feedback[index] = cards[index].GetComponent<PedalButtonFeedback>();
            if (feedback[index] == null) feedback[index] = cards[index].gameObject.AddComponent<PedalButtonFeedback>();
            feedback[index].Configure(Accent, index == 0 ? RacingPanelGraphic.SurfaceStyle.Primary : RacingPanelGraphic.SurfaceStyle.Secondary);
        }
        SetState(0, 0f);
    }

    public void SetState(int optionIndex, float pedal)
    {
        if (feedback == null) return;
        int selected = Mathf.Clamp(optionIndex, 0, feedback.Length - 1);
        for (int index = 0; index < feedback.Length; index++)
        {
            float amount = index == selected ? Mathf.Clamp01(pedal) : 0f;
            feedback[index].SetState(index == selected, amount, Accent);
        }
    }

    public void PlayConfirm(int index)
    {
        if (feedback != null && index >= 0 && index < feedback.Length) feedback[index].PlayConfirm();
    }
}
