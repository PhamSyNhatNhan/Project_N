using System;
using System.Collections.Generic;

/// <summary>
/// Dữ liệu persistent của user — lưu ra UserData.json.
/// Chứa số Shard, CharacterShard và danh sách Talent đã unlock.
/// </summary>
[Serializable]
public class UserData
{
    public int          shards           = 0;
    public int          characterShards  = 0;
    public List<string> unlockedTalents  = new List<string>();
}