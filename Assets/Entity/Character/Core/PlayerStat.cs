using UnityEngine;

public class PlayerStat : Stat
{
    public override void ApplyData()
    {
        base.ApplyData();
        LoadAvatar();
    }

    private void LoadAvatar()
    {
        if (rawStatData == null) return;

        string path = rawStatData.Get<string>("entityIconPath");
        if (string.IsNullOrEmpty(path)) return;

        var sprite = Resources.Load<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogWarning($"[PlayerStat] Không tìm thấy avatar: '{path}'");
            return;
        }

        EventManager.Entity.OnEntityAvatarLoaded
            .Get($"{NameCharacter}_{GetInstanceID()}")
            .Invoke(this, sprite);
    }
}