using UnityEngine;

/// <summary>
/// Gắn trên Pause Button trong game UI.
/// Fire OnPause event khi bấm.
/// </summary>
public class PauseButton : MonoBehaviour
{
    public void OnClick()
    {
        EventManager.Gm.OnPause.Get().Invoke(this, null);
    }
}