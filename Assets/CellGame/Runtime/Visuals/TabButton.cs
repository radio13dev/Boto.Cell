using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class TabButton : MonoBehaviour
{
    public static bool TabToggleRequested = false;
    public void ToggleTabMode() => TabButton.TabToggleRequested = true;

    private void Update()
    {
        if (Keyboard.current.tabKey.wasPressedThisFrame) ToggleTabMode();
    }
}