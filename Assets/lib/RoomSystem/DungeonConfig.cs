using System;

/// <summary>
/// Cấu hình 1 wave trong phòng chiến đấu.
/// </summary>
[Serializable]
public class WaveConfig
{
    /// <summary>Danh sách enemy id spawn trong wave này.</summary>
    public string[] enemyPool;

    /// <summary>Delay trước khi wave bắt đầu spawn (giây).</summary>
    public float spawnDelay = 3f;
}

/// <summary>
/// Cấu hình 1 tầng trong dungeon.
/// </summary>
[Serializable]
public class FloorConfig
{
    public int           floorId;
    public RoomType      roomType;

    /// <summary>Chỉ dùng khi roomType == Boss.</summary>
    public BossId?       bossId;

    /// <summary>Scene cần load cho tầng này — phải có trong Build Settings.</summary>
    public string        sceneName;

    /// <summary>Hệ số scale stat cho enemy trong tầng này.</summary>
    public ScalingConfig scaling;

    /// <summary>Danh sách wave. Null hoặc rỗng nếu roomType == Shop.</summary>
    public WaveConfig[]  waves;
}

/// <summary>
/// Toàn bộ cấu hình dungeon — deserialize từ {groupId}.json.
/// </summary>
[Serializable]
public class DungeonConfig
{
    /// <summary>Tên hiển thị của nhóm map.</summary>
    public string        displayName;

    /// <summary>Mô tả ngắn.</summary>
    public string        description;

    /// <summary>Path đến preview image trong Resources.</summary>
    public string        previewPath;

    public FloorConfig[] floors;
}