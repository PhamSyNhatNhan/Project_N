using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gắn trên Button "Character Info" trong Hall scene.
/// Bấm → fire OnOpenCharacterInfo → HallUIManager.HandleOpenCharacterInfo → CharacterInfoUI.Show().
/// </summary>
[RequireComponent(typeof(Button))]
public class CharacterInfoButton : MonoBehaviour
{
    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClick);
    }

    private void OnDestroy()
    {
        _button.onClick.RemoveListener(OnClick);
    }

    private void OnClick()
    {
        EventManager.Gm.OnOpenCharacterInfo.Get().Invoke(this, null);
    }
}