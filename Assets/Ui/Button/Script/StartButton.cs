using UnityEngine;

public class StartButton : MonoBehaviour
{
    public void LoadScene()
    {
        EventManager.Ui.TriggerLoadingScene.Get().Invoke(this, "Hall");
    }
}