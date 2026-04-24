using UnityEngine;

/// <summary>
/// Display data của nhân vật — load từ StatData.extraFields trong JSON.
/// Dùng cho CharacterSelectUI.
/// </summary>
public class PlayerDisplayData
{
    public Talent  talent;
    public string  displayName;
    public string  description;
    public Sprite     avatar;    // entityIconPath — icon nhỏ
    public Sprite     portrait;  // portraitPath — hình lớn cho CharacterSelect
    public GameObject prefab;
}