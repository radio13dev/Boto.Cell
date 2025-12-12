using System;
using System.Collections;
using Drawing;
using UnityEngine;
using UnityEngine.InputSystem;

public class Shop : MonoBehaviour
{
    public static Shop Instance;
    public const float AnimationTransitionTime = 0.2f;

    bool m_IsOpen;
    ExclusiveCoroutine co;
    
    public ease.Mode Easing = ease.Mode.cubic_out;
    public TransitionPoint ClosedTransform;
    public TransitionPoint OpenTransform;
    

    private void Awake()
    {
        Instance = this;
        ClosedTransform.Apply((RectTransform)transform);
    }

    public void Toggle()
    {
        if (m_IsOpen)
        {
            co.StartCoroutine(this, ClosedTransform.Lerp((RectTransform)transform, AnimationTransitionTime, Easing));
        }
        else
            co.StartCoroutine(this, OpenTransform.Lerp((RectTransform)transform, AnimationTransitionTime, Easing));
        m_IsOpen = !m_IsOpen;
    }
}
