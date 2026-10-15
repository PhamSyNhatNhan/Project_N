using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    protected virtual void OnEnable()
    {
        EventManager.Gm.TriggerGenericEvent.Get("Scene").AddListener((component, data) => ChangeScene((String)data));
    }

    protected virtual void OnDisable()
    {
        EventManager.Gm.TriggerGenericEvent.Get("Scene").RemoveListener((component, data) => ChangeScene((String)data));

    }
    
    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
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
