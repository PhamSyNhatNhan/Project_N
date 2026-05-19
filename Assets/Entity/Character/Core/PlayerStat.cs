using UnityEngine;

public class PlayerStat : Stat
{
    public override void ApplyData()
    {
        base.ApplyData();
        LoadAvatar();
        FireHealthEvent();
    }

    protected override void OnDead()
    {
        CanDamge = false;
        EventManager.Gm.OnPlayerDead.Get().Invoke(this, null);
    }

    private void FireHealthEvent()
    {
        float buffedMax = BuffHealth.GetFinalValue(MaxHealth);
        EventManager.Entity.OnEntityHealthChanged
            .Get(entityKey)
            .Invoke(this, buffedMax > 0f ? CurHealth / buffedMax : 0f);
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