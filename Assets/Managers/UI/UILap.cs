using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class UILap
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text lapText;
    [SerializeField] private int currentLap = 1;
    private UIValuePulse valuePulse;
    private UIRevealSequence progressMotion;
    private TMP_Text totalText;
    private Image[] progressSegments = Array.Empty<Image>();
    private Color segmentColor;
    public void SetTotalLaps(int total)
    {
        if (totalText != null) totalText.text = "/ " + Mathf.Max(1, total);
    }

    public void Init(Transform parent)
    {
        if (parent != null)
        {
            root = parent.gameObject;
        }

        Transform rootTransform = root != null ? root.transform : parent;
        if (rootTransform != null)
        {
            Transform textTransform = rootTransform.Find("Txt");
            lapText = textTransform != null
                ? textTransform.GetComponentInChildren<TMP_Text>(true)
                : rootTransform.GetComponentInChildren<TMP_Text>(true);
        }

        if (lapText != null)
        {
            valuePulse = lapText.GetComponent<UIValuePulse>();
            if (valuePulse == null)
            {
                valuePulse = lapText.gameObject.AddComponent<UIValuePulse>();
            }
        }

        totalText = rootTransform != null ? rootTransform.Find("Total")?.GetComponent<TMP_Text>() : null;
        Transform ticks = rootTransform != null ? rootTransform.Find("Ticks") : null;
        progressSegments = ticks != null ? ticks.GetComponentsInChildren<Image>(true) : Array.Empty<Image>();
        if(ticks!=null){progressMotion=ticks.GetComponent<UIRevealSequence>();if(progressMotion==null)progressMotion=ticks.gameObject.AddComponent<UIRevealSequence>();progressMotion.Configure(ticks,new[]{"Left","Right"},new Vector2(6,0),.04f,.20f);}
        segmentColor = ArcadeUI.For(rootTransform).Ink;
        UpdateText();
    }

    public void SetLap(int lap)
    {
        lap = Mathf.Max(1, lap);

        bool changed=lap!=currentLap;
        if (changed)
        {
            LapTextAnimation(currentLap, lap);
            currentLap = lap;
        }

        UpdateText();
        if(changed&&Application.isPlaying)progressMotion?.Play();
    }

    private void LapTextAnimation(int oldLap, int newLap)
    {
        if(!Application.isPlaying)return;
        valuePulse?.Play(new Color(1f, 0.84f, 0.12f, 1f), -12f);
    }

    public void SetActive(bool isActive)
    {
        if (root != null)
        {
            root.SetActive(isActive);
        }
    }

    private void UpdateText()
    {
        if (lapText != null)
        {
            lapText.text = currentLap.ToString();
        }
        // The two segments indicate the current lap, rather than distance around the course.
        for (int i = 0; i < progressSegments.Length; i++)
        {
            Color tint = segmentColor;
            tint.a = currentLap > i ? 1f : .18f;
            progressSegments[i].color = tint;
        }
    }
}
