using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Button))]
public class AddShardsButton : MonoBehaviour
{
    [SerializeField] private int amount = 15000;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        UpdateVisibility();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        GetComponent<Button>().interactable = SceneManager.GetActiveScene().name == "Hall";
    }

    private void OnClick()
    {
        UserDataManager.Instance?.AddShards(amount);
    }
}