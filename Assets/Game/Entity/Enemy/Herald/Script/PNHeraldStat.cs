#if UNITY_5_3_OR_NEWER
using UnityEngine;

// Herald's stat extension point. Values and enemy classification stay content-owned (characters/HeraldCharacter.json).
[DisallowMultipleComponent]
public class PNHeraldStat : PNEnemyStat
{
}
#endif
