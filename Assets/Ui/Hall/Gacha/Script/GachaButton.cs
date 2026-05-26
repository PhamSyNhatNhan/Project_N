using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn trên Button "Gacha" trong Hall scene.
/// Bấm → fire OnOpenGacha → HallUIManager → GachaUI.Show().
/// </summary>
[RequireComponent(typeof(Button))]
public class GachaButton : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
            EventManager.Gm.OnOpenGacha.Get().Invoke(this, null));
    }
}