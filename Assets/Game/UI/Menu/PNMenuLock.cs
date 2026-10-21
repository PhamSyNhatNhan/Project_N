#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;

// EN: Input/time lock of the mode menu. Same behaviour as the pause menu (local owner's movement and skills gated, mobile input reset,
//     game time frozen only in an offline session, presses made under the menu ignored until physically released) but a separate
//     class: the pause menu is not touched. Tick must run every frame, also while the lock is not held, to rearm released input.
// VI: Khóa input/thời gian của menu chế độ, cùng hành vi với pause nhưng là lớp riêng.
internal sealed class PNMenuLock
{
    private readonly PNUiRuntime owner;
    private readonly List<PNPlayerMove> moves = new List<PNPlayerMove>();
    private readonly List<PNPlayerSkill> skills = new List<PNPlayerSkill>();
    private bool ownsTime;
    private float previousTime;
    public bool Held { get; private set; }

    public PNMenuLock(PNUiRuntime owner) { this.owner = owner; }

    public void Acquire()
    {
        if (Held) return;
        Held = true; owner.ResetMobileInput(); Grab();
        if (EveRegistrySession.IsReady && EveRegistrySession.Context.Role == EveSessionRole.Offline)
        { previousTime = Time.timeScale; ownsTime = true; Time.timeScale = 0; }
    }

    public void Tick()
    {
        if (Held) Grab(); else Neutral();
    }

    public void Release()
    {
        if (!Held) return;
        Held = false; RestoreTime(); owner.ResetMobileInput(); Neutral();
    }

    // Session ended or the owner is going away: free everything at once.
    public void Abort()
    {
        Held = false; RestoreTime(); owner.ResetMobileInput();
        foreach (var move in moves) if (move != null) move.MenuInputBlocked = false;
        foreach (var skill in skills) if (skill != null) skill.MenuInputBlocked = false;
        moves.Clear(); skills.Clear();
    }

    private void Grab()
    {
        if (!EveRegistrySession.IsReady) return;
        foreach (var loader in EveRegistrySession.Context.Entities.Snapshot())
        {
            var identity = loader.GetComponent<EveEntityIdentity>();
            if (identity == null || identity.OwnerId != owner.LocalOwnerId || loader.GetComponent<PNPlayerStat>() == null) continue;
            var move = loader.GetComponent<PNPlayerMove>();
            if (move != null && !moves.Contains(move)) { moves.Add(move); move.MenuInputBlocked = true; move.SetMoveDirection(Vector2.zero); }
            var skill = loader.GetComponent<PNPlayerSkill>();
            if (skill != null && !skills.Contains(skill)) { skills.Add(skill); skill.MenuInputBlocked = true; }
        }
    }

    private static bool Held_(InputAction action)
    {
        if (action == null) return false;
        foreach (var control in action.controls) if (control.EvaluateMagnitude() > .2f) return true;
        return false;
    }

    private static bool Held_(PNPlayerSkill skill)
    {
        var c = skill.PC?.Controller;
        return c != null && (Held_(c.Attack) || Held_(c.Skill) || Held_(c.Ulti) || Held_(c.Dash) || Held_(c.Burst));
    }

    private void Neutral()
    {
        for (int i = moves.Count - 1; i >= 0; i--)
            if (moves[i] == null || !moves[i].HasHeldMenuInput)
            { if (moves[i] != null) moves[i].MenuInputBlocked = false; moves.RemoveAt(i); }
        for (int i = skills.Count - 1; i >= 0; i--)
            if (skills[i] == null || !Held_(skills[i]))
            { if (skills[i] != null) skills[i].MenuInputBlocked = false; skills.RemoveAt(i); }
    }

    private void RestoreTime() { if (!ownsTime) return; ownsTime = false; Time.timeScale = previousTime; }
}
#endif
