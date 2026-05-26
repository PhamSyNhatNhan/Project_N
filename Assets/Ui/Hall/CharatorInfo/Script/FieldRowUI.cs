using TMPro;
using UnityEngine;

/// <summary>
/// Prefab 1 dòng field trong SkillDetailPopup.
/// Layout: KeyText (trái) | ValueText (phải).
/// </summary>
public class FieldRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI keyText;
    [SerializeField] private TextMeshProUGUI valueText;

    public void Setup(string key, string value, Color valueColor)
    {
        if (keyText   != null) keyText.text    = key;
        if (valueText != null)
        {
            valueText.text  = value;
            valueText.color = valueColor;
        }
    }
}