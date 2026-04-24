using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    protected virtual void OnEnable()
    {
        EventManager.Gm.TriggerGenericEvent.Get("Scene").AddListener((component, data) => ChangeScene((String)data));
    }

    protected virtual void OnDisable()
    {
        EventManager.Gm.TriggerGenericEvent.Get("Scene").RemoveListener((component, data) => ChangeScene((String)data));

    }

    protected virtual void Start()
    {
        if (Application.isMobilePlatform)
        {
            Application.targetFrameRate = 120;
        }
        else
        {
            Application.targetFrameRate = 120;
        }
    }

    protected virtual void Update()
    {
        
    }

    private void ChangeScene(String sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
