using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameMode
{
    Auditorium,
    ER,    
}

public enum DamageType
{
    True,
    Physical,
    Magic
}

public enum IFrameMode
{
    Legacy,    
    Gradual   
}

public enum HoldMode { None, Hold, SlowTap, Repeat }

public enum EnemyType
{
    Normal,
    Elite,
    Boss
}

public enum Talent
{
    Test,
    Gura,
    Sakuya
}

public enum SkillType
{
    Attack,
    Skill,
    Ulti,
    Dash,
    Burst,
    Passive
}

public enum ObjectSkillType
{
    projectile,
    bullet,
}

public enum SlowAffectType
{
    Both,
    Movement,
    Animation
}

public enum BulletMoveMode    { Forward, Angle, Target, Homing, Custom }
public enum BulletDisableMode { Timer, Distance }

public enum ChargeMode
{
    PerStack,
    AllAtOnce
}

public enum EffectCategory { Buff, Debuff, Neutral }

public enum EffectType
{
    Burn, Poison, Slow, Stun, Freeze, Bleed, Shock, Shield, Immortal
}

// ── Room ────────────────────────────────────────────────────
public enum DungeonGroupId {Calamity}
public enum RoomType { NormalCombat, Shop, Boss }
public enum RoomState { Waiting, InProgress, Completed }
public enum BossId {Boss_1, Brimstone, Calamitas}

 
// ── Rogue Buff ────────────────────────────────────────────────────
public enum BuffGroupId
{
    Warrior, Mage, Assassin
    
}
