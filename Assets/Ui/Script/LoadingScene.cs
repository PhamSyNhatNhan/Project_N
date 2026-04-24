using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScene : MonoBehaviour
{
    [SerializeField] private GameObject loadingScreenUI;

    private AsyncOperation async;

    private void OnEnable()
    {
        EventManager.Ui.TriggerLoadingScene.Get().AddListener((component, data) => LoadScreen(data));
    }
    
    private void OnDisable()
    {
        EventManager.Ui.TriggerLoadingScene.Get().RemoveListener((component, data) => LoadScreen(data));
    }

    private void LoadScreen(object SceneToLoad)
    {
        Debug.Log("LoadingScreen 1");
        StartCoroutine(LoadSceneAsync(SceneToLoad));
    }
    
    private IEnumerator LoadSceneAsync(object SceneToLoad)
    {
        loadingScreenUI.SetActive(true);
        int sceneToLoad = (int)SceneToLoad;
       
        //yield return new WaitForSeconds(1f);

        async = SceneManager.LoadSceneAsync(sceneToLoad);
        async.allowSceneActivation = false;
        
        while (async.isDone == false)
        {
            if (async.progress >= 0.9f)
            {
                async.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}