using UnityEngine;

public class FullscreenButton : MonoBehaviour
{
    public void ToggleFullscreen() => Screen.fullScreen = !Screen.fullScreen;
}
