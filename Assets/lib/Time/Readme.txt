// Broadcast slow đến tất cả entity (AoE, zone...)
EventManager.Time.OnSlowApply.Get().Invoke(this,
    SlowData.Create("blizzard", 0.2f, SlowAffectType.Both)
);

// Gọi thẳng trên entity cụ thể (single target)
enemy.GetComponent<TimeScale>().AddModifier("slow_hit", 0.5f);

// Pause game
EventManager.Time.OnGamePause.Get().Invoke(this, true);

// UI theo dõi scale của player
EventManager.Time.OnEntityScaleChanged
    .Get("Player")
    .AddListener((sender, data) => UpdateUI((float)data));