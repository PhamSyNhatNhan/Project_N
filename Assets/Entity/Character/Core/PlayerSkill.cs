using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;

public class PlayerSkill : Skill
{

    // ═══════════════════════════════════════════════════════════════════
    //  INSPECTOR FIELDS
    // ═══════════════════════════════════════════════════════════════════

    [Header("Attack Button")]
    [SerializeField] protected bool     canTapAttack   = true;
    [SerializeField] protected HoldMode holdModeAttack = HoldMode.None;

    [Header("Skill Button")]
    [SerializeField] protected bool     canTapSkill   = true;
    [SerializeField] protected HoldMode holdModeSkill = HoldMode.None;

    [Header("Ulti Button")]
    [SerializeField] protected bool     canTapUlti   = true;
    [SerializeField] protected HoldMode holdModeUlti = HoldMode.None;

    [Header("Dash Button")]
    [SerializeField] protected bool     canTapDash   = true;
    [SerializeField] protected HoldMode holdModeDash = HoldMode.None;

    [Header("Burst Button")]
    [SerializeField] protected bool     canTapBurst   = true;
    [SerializeField] protected HoldMode holdModeBurst = HoldMode.None;

    [Header("Debug Log")]
    [SerializeField] private bool debugAttack = true;
    [SerializeField] private bool debugSkill  = true;
    [SerializeField] private bool debugUlti   = true;
    [SerializeField] private bool debugDash   = true;
    [SerializeField] private bool debugBurst  = true;

    [Header("Setup")]
    [SerializeField] protected LayerMask enemyLayer;
    protected bool canInput = true;

    // ═══════════════════════════════════════════════════════════════════
    //  INPUT
    // ═══════════════════════════════════════════════════════════════════

    private PlayerControls pc;

    // ═══════════════════════════════════════════════════════════════════
    //  TIMING
    // ═══════════════════════════════════════════════════════════════════

    private float _slowTapDurationAttack = 0f; private float _slowTapDurationSkill = 0f;
    private float _slowTapDurationUlti   = 0f; private float _slowTapDurationDash  = 0f;
    private float _slowTapDurationBurst  = 0f;

    private float _repeatDelayAttack = 0f;  private float _repeatIntervalAttack = 0f;
    private float _repeatDelaySkill  = 0f;  private float _repeatIntervalSkill  = 0f;
    private float _repeatDelayUlti   = 0f;  private float _repeatIntervalUlti   = 0f;
    private float _repeatDelayDash   = 0f;  private float _repeatIntervalDash   = 0f;
    private float _repeatDelayBurst  = 0f;  private float _repeatIntervalBurst  = 0f;

    // ═══════════════════════════════════════════════════════════════════
    //  STATE FLAGS
    // ═══════════════════════════════════════════════════════════════════

    [Header("Attack")]  protected bool isAttack = false; protected bool canAttack = true;
    [Header("Ulti")]    protected bool isUlti   = false; protected bool canUlti   = true;
    [Header("Dash")]    protected bool isDash   = false; protected bool canDash   = true;
    [Header("Burst")]   protected bool isBurst  = false; protected bool canBurst  = true;
    protected bool isBurstMode = false;
    
    // ═══════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        base.Awake();
        AwakeSetUp();
    }

    protected override void Start()
    {
        try { pc = GetComponent<PlayerController>().PC; }
        catch (Exception e) { Debug.Log(e); }

        StartSetUp();

        _ApplySetup(pc.Controller.Attack, canTapAttack, holdModeAttack, _slowTapDurationAttack, _repeatDelayAttack, _repeatIntervalAttack);
        _ApplySetup(pc.Controller.Skill,  canTapSkill,  holdModeSkill,  _slowTapDurationSkill,  _repeatDelaySkill,  _repeatIntervalSkill);
        _ApplySetup(pc.Controller.Ulti,   canTapUlti,   holdModeUlti,   _slowTapDurationUlti,   _repeatDelayUlti,   _repeatIntervalUlti);
        _ApplySetup(pc.Controller.Dash,   canTapDash,   holdModeDash,   _slowTapDurationDash,   _repeatDelayDash,   _repeatIntervalDash);
        _ApplySetup(pc.Controller.Burst,  canTapBurst,  holdModeBurst,  _slowTapDurationBurst,  _repeatDelayBurst,  _repeatIntervalBurst);

        pc.Controller.Attack.started   += ctx => StartedAttack(ctx);
        pc.Controller.Attack.performed += ctx => PerformedAttack(ctx);
        pc.Controller.Attack.canceled  += ctx => CanceledAttack(ctx);

        pc.Controller.Skill.started    += ctx => StartedSkill(ctx);
        pc.Controller.Skill.performed  += ctx => PerformedSkill(ctx);
        pc.Controller.Skill.canceled   += ctx => CanceledSkill(ctx);

        pc.Controller.Ulti.started     += ctx => StartedUlti(ctx);
        pc.Controller.Ulti.performed   += ctx => PerformedUlti(ctx);
        pc.Controller.Ulti.canceled    += ctx => CanceledUlti(ctx);

        pc.Controller.Dash.started     += ctx => StartedDash(ctx);
        pc.Controller.Dash.performed   += ctx => PerformedDash(ctx);
        pc.Controller.Dash.canceled    += ctx => CanceledDash(ctx);

        pc.Controller.Burst.started    += ctx => StartedBurst(ctx);
        pc.Controller.Burst.performed  += ctx => PerformedBurst(ctx);
        pc.Controller.Burst.canceled   += ctx => CanceledBurst(ctx);
    }

    // Update từ Skill base — tự tick cdMap
    protected override void Update()
    {
        base.Update();
    }

    protected void OnValidate()
    {
        if (!Application.isPlaying || pc == null) return;
        _ReapplyAttack();
        _ReapplySkill();
        _ReapplyUlti();
        _ReapplyDash();
        _ReapplyBurst();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  APPLY BINDING  (internal)
    // ═══════════════════════════════════════════════════════════════════

    private static void _ApplySetup(InputAction action,
        bool canTap, HoldMode mode,
        float slowTapDuration,
        float repeatDelay, float repeatInterval)
    {
        string str = _BuildString(canTap, mode, slowTapDuration, repeatDelay, repeatInterval);
        if (string.IsNullOrEmpty(str)) return;

        for (int i = 0; i < action.bindings.Count; i++)
        {
            if (action.bindings[i].isPartOfComposite) continue;
            var ov = action.bindings[i];
            ov.overrideInteractions = str;
            action.ApplyBindingOverride(i, ov);
        }
    }

    private static string _BuildString(bool canTap, HoldMode mode,
        float slowTapDuration, float repeatDelay, float repeatInterval)
    {
        var sb = new System.Text.StringBuilder();
        var ic = System.Globalization.CultureInfo.InvariantCulture;

        if (canTap) sb.Append("tap()");

        switch (mode)
        {
            case HoldMode.Hold:
                if (sb.Length > 0) sb.Append(';');
                sb.Append("hold()");
                break;

            case HoldMode.SlowTap:
                if (sb.Length > 0) sb.Append(';');
                sb.Append("slowTap(");
                if (slowTapDuration > 0) sb.Append($"duration={slowTapDuration.ToString(ic)},");
                if (sb[sb.Length - 1] == ',') sb.Length--;
                sb.Append(')');
                break;

            case HoldMode.Repeat:
                if (sb.Length > 0) sb.Append(';');
                sb.Append("Repeat(");
                if (repeatDelay    > 0) sb.Append($"delay={repeatDelay.ToString(ic)},");
                if (repeatInterval > 0) sb.Append($"interval={repeatInterval.ToString(ic)},");
                if (sb[sb.Length - 1] == ',') sb.Length--;
                sb.Append(')');
                break;
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EASY SKILL MODIFY
    // ═══════════════════════════════════════════════════════════════════

    protected void EasySkillModify(string btn, string holdMode) => EasySkillModify(btn, _ParseMode(holdMode));
    protected void EasySkillModify(string btn, HoldMode mode)
    {
        switch (btn.ToLower())
        {
            case "attack": holdModeAttack = mode; _ReapplyAttack(); break;
            case "skill":  holdModeSkill  = mode; _ReapplySkill();  break;
            case "ulti":   holdModeUlti   = mode; _ReapplyUlti();   break;
            case "dash":   holdModeDash   = mode; _ReapplyDash();   break;
            case "burst":  holdModeBurst  = mode; _ReapplyBurst();  break;
            default: Debug.LogWarning($"[EasySkillModify] '{btn}' không hợp lệ"); return;
        }
    }

    protected void EasySkillModify(string btn, bool canTap)
    {
        switch (btn.ToLower())
        {
            case "attack": canTapAttack = canTap; _ReapplyAttack(); break;
            case "skill":  canTapSkill  = canTap; _ReapplySkill();  break;
            case "ulti":   canTapUlti   = canTap; _ReapplyUlti();   break;
            case "dash":   canTapDash   = canTap; _ReapplyDash();   break;
            case "burst":  canTapBurst  = canTap; _ReapplyBurst();  break;
            default: Debug.LogWarning($"[EasySkillModify] '{btn}' không hợp lệ"); return;
        }
    }

    protected void EasySkillModify(string btn, string holdMode, bool canTap)
    {
        EasySkillModify(btn, canTap);
        EasySkillModify(btn, _ParseMode(holdMode));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EASY SLOW TAP DURATION
    // ═══════════════════════════════════════════════════════════════════

    protected void EasyAttackSlowTapDuration(float v) { _slowTapDurationAttack = v; _ReapplyAttack(); }
    protected void EasySkillSlowTapDuration (float v) { _slowTapDurationSkill  = v; _ReapplySkill();  }
    protected void EasyUltiSlowTapDuration  (float v) { _slowTapDurationUlti   = v; _ReapplyUlti();   }
    protected void EasyDashSlowTapDuration  (float v) { _slowTapDurationDash   = v; _ReapplyDash();   }
    protected void EasyBurstSlowTapDuration (float v) { _slowTapDurationBurst  = v; _ReapplyBurst();  }

    // ═══════════════════════════════════════════════════════════════════
    //  EASY REPEAT
    // ═══════════════════════════════════════════════════════════════════

    protected void EasyAttackRepeatDelay(float v) { _repeatDelayAttack = v; _ReapplyAttack(); }
    protected void EasySkillRepeatDelay (float v) { _repeatDelaySkill  = v; _ReapplySkill();  }
    protected void EasyUltiRepeatDelay  (float v) { _repeatDelayUlti   = v; _ReapplyUlti();   }
    protected void EasyDashRepeatDelay  (float v) { _repeatDelayDash   = v; _ReapplyDash();   }
    protected void EasyBurstRepeatDelay (float v) { _repeatDelayBurst  = v; _ReapplyBurst();  }

    protected void EasyAttackRepeatInterval(float v) { _repeatIntervalAttack = v; _ReapplyAttack(); }
    protected void EasySkillRepeatInterval (float v) { _repeatIntervalSkill  = v; _ReapplySkill();  }
    protected void EasyUltiRepeatInterval  (float v) { _repeatIntervalUlti   = v; _ReapplyUlti();   }
    protected void EasyDashRepeatInterval  (float v) { _repeatIntervalDash   = v; _ReapplyDash();   }
    protected void EasyBurstRepeatInterval (float v) { _repeatIntervalBurst  = v; _ReapplyBurst();  }

    protected void EasyAttackRepeat(float delay, float interval) { _repeatDelayAttack = delay; _repeatIntervalAttack = interval; _ReapplyAttack(); }
    protected void EasySkillRepeat (float delay, float interval) { _repeatDelaySkill  = delay; _repeatIntervalSkill  = interval; _ReapplySkill();  }
    protected void EasyUltiRepeat  (float delay, float interval) { _repeatDelayUlti   = delay; _repeatIntervalUlti   = interval; _ReapplyUlti();   }
    protected void EasyDashRepeat  (float delay, float interval) { _repeatDelayDash   = delay; _repeatIntervalDash   = interval; _ReapplyDash();   }
    protected void EasyBurstRepeat (float delay, float interval) { _repeatDelayBurst  = delay; _repeatIntervalBurst  = interval; _ReapplyBurst();  }

    // ═══════════════════════════════════════════════════════════════════
    //  REAPPLY SHORTCUTS
    // ═══════════════════════════════════════════════════════════════════

    private void _ReapplyAttack() => _ApplySetup(pc.Controller.Attack, canTapAttack, holdModeAttack, _slowTapDurationAttack, _repeatDelayAttack, _repeatIntervalAttack);
    private void _ReapplySkill()  => _ApplySetup(pc.Controller.Skill,  canTapSkill,  holdModeSkill,  _slowTapDurationSkill,  _repeatDelaySkill,  _repeatIntervalSkill);
    private void _ReapplyUlti()   => _ApplySetup(pc.Controller.Ulti,   canTapUlti,   holdModeUlti,   _slowTapDurationUlti,   _repeatDelayUlti,   _repeatIntervalUlti);
    private void _ReapplyDash()   => _ApplySetup(pc.Controller.Dash,   canTapDash,   holdModeDash,   _slowTapDurationDash,   _repeatDelayDash,   _repeatIntervalDash);
    private void _ReapplyBurst()  => _ApplySetup(pc.Controller.Burst,  canTapBurst,  holdModeBurst,  _slowTapDurationBurst,  _repeatDelayBurst,  _repeatIntervalBurst);

    private static HoldMode _ParseMode(string s) => s.ToLower() switch
    {
        "hold"    => HoldMode.Hold,
        "slowtap" => HoldMode.SlowTap,
        "repeat"  => HoldMode.Repeat,
        _         => HoldMode.None,
    };

    // ═══════════════════════════════════════════════════════════════════
    //  VIRTUAL SETUP
    // ═══════════════════════════════════════════════════════════════════

    protected virtual void StartSetUp() { }
    protected virtual void AwakeSetUp() { }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPER
    // ═══════════════════════════════════════════════════════════════════

    private static bool IsTap(InputAction.CallbackContext ctx)     => ctx.interaction is TapInteraction;
    private static bool IsHold(InputAction.CallbackContext ctx)    => ctx.interaction is HoldInteraction;
    private static bool IsSlowTap(InputAction.CallbackContext ctx) => ctx.interaction is SlowTapInteraction;
    private static bool IsRepeat(InputAction.CallbackContext ctx)  => ctx.interaction is RepeatInteraction;

    // ═══════════════════════════════════════════════════════════════════
    //  ATTACK
    // ═══════════════════════════════════════════════════════════════════
    #region Attack

    protected virtual void StartedAttack(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapAttack)                       StartTapAttack();
        else if (IsHold(ctx)    && holdModeAttack == HoldMode.Hold)    StartHoldAttack();
        else if (IsSlowTap(ctx) && holdModeAttack == HoldMode.SlowTap) StartSlowTapAttack();
        else if (IsRepeat(ctx)  && holdModeAttack == HoldMode.Repeat)  StartRepeatAttack();
    }

    protected virtual void PerformedAttack(InputAction.CallbackContext ctx)
    {
        if (IsTap(ctx) && canTapAttack)
        { EnterTapAttack(); TapAttack(); ExitTapAttack(); }
        else if (IsHold(ctx) && holdModeAttack == HoldMode.Hold)
        { EnterHoldAttack(); HoldAttack(); ExitHoldAttack(); }
        else if (IsSlowTap(ctx) && holdModeAttack == HoldMode.SlowTap)
        { EnterSlowTapAttack(); SlowTapAttack(); ExitSlowTapAttack(); }
        else if (IsRepeat(ctx) && holdModeAttack == HoldMode.Repeat)
        { RepeatAttack(); }
    }

    protected virtual void CanceledAttack(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapAttack)                       CanceledTapAttack();
        else if (IsHold(ctx)    && holdModeAttack == HoldMode.Hold)    CanceledHoldAttack();
        else if (IsSlowTap(ctx) && holdModeAttack == HoldMode.SlowTap) CanceledSlowTapAttack();
        else if (IsRepeat(ctx)  && holdModeAttack == HoldMode.Repeat)  CanceledRepeatAttack();
    }

    protected virtual void StartTapAttack()     { if (debugAttack) Debug.Log("Start Tap Attack"); }
    protected virtual void StartHoldAttack()    { if (debugAttack) Debug.Log("Start Hold Attack"); }
    protected virtual void StartSlowTapAttack() { if (debugAttack) Debug.Log("Start SlowTap Attack"); }
    protected virtual void StartRepeatAttack()  { if (debugAttack) Debug.Log("Start Repeat Attack"); }

    protected virtual void EnterTapAttack()     { if (debugAttack) Debug.Log("Enter Tap Attack"); }
    protected virtual void EnterHoldAttack()    { if (debugAttack) Debug.Log("Enter Hold Attack"); }
    protected virtual void EnterSlowTapAttack() { if (debugAttack) Debug.Log("Enter SlowTap Attack"); }

    protected virtual void TapAttack()     { if (debugAttack) Debug.Log("Tap Attack");     EventManager.Player.OnPlayerAttack.Get("").Invoke(this, null); }
    protected virtual void HoldAttack()    { if (debugAttack) Debug.Log("Hold Attack");    EventManager.Player.OnPlayerAttack.Get("").Invoke(this, null); }
    protected virtual void SlowTapAttack() { if (debugAttack) Debug.Log("SlowTap Attack"); EventManager.Player.OnPlayerAttack.Get("").Invoke(this, null); }
    protected virtual void RepeatAttack()  { if (debugAttack) Debug.Log("Repeat Attack");  EventManager.Player.OnPlayerAttack.Get("").Invoke(this, null); }

    protected virtual void ExitTapAttack()     { if (debugAttack) Debug.Log("Exit Tap Attack"); }
    protected virtual void ExitHoldAttack()    { if (debugAttack) Debug.Log("Exit Hold Attack"); }
    protected virtual void ExitSlowTapAttack() { if (debugAttack) Debug.Log("Exit SlowTap Attack"); }

    protected virtual void CanceledTapAttack()     { if (debugAttack) Debug.Log("Canceled Tap Attack"); }
    protected virtual void CanceledHoldAttack()    { if (debugAttack) Debug.Log("Canceled Hold Attack"); }
    protected virtual void CanceledSlowTapAttack() { if (debugAttack) Debug.Log("Canceled SlowTap Attack"); }
    protected virtual void CanceledRepeatAttack()  { if (debugAttack) Debug.Log("Canceled Repeat Attack"); }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    //  SKILL
    // ═══════════════════════════════════════════════════════════════════
    #region Skill

    protected virtual void StartedSkill(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapSkill)                       StartTapSkill();
        else if (IsHold(ctx)    && holdModeSkill == HoldMode.Hold)    StartHoldSkill();
        else if (IsSlowTap(ctx) && holdModeSkill == HoldMode.SlowTap) StartSlowTapSkill();
        else if (IsRepeat(ctx)  && holdModeSkill == HoldMode.Repeat)  StartRepeatSkill();
    }

    protected virtual void PerformedSkill(InputAction.CallbackContext ctx)
    {
        if (IsTap(ctx) && canTapSkill)
        { EnterTapSkill(); TapSkill(); ExitTapSkill(); }
        else if (IsHold(ctx) && holdModeSkill == HoldMode.Hold)
        { EnterHoldSkill(); HoldSkill(); ExitHoldSkill(); }
        else if (IsSlowTap(ctx) && holdModeSkill == HoldMode.SlowTap)
        { EnterSlowTapSkill(); SlowTapSkill(); ExitSlowTapSkill(); }
        else if (IsRepeat(ctx) && holdModeSkill == HoldMode.Repeat)
        { RepeatSkill(); }
    }

    protected virtual void CanceledSkill(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapSkill)                       CanceledTapSkill();
        else if (IsHold(ctx)    && holdModeSkill == HoldMode.Hold)    CanceledHoldSkill();
        else if (IsSlowTap(ctx) && holdModeSkill == HoldMode.SlowTap) CanceledSlowTapSkill();
        else if (IsRepeat(ctx)  && holdModeSkill == HoldMode.Repeat)  CanceledRepeatSkill();
    }

    protected virtual void StartTapSkill()     { if (debugSkill) Debug.Log("Start Tap Skill"); }
    protected virtual void StartHoldSkill()    { if (debugSkill) Debug.Log("Start Hold Skill"); }
    protected virtual void StartSlowTapSkill() { if (debugSkill) Debug.Log("Start SlowTap Skill"); }
    protected virtual void StartRepeatSkill()  { if (debugSkill) Debug.Log("Start Repeat Skill"); }

    protected virtual void EnterTapSkill()     { if (debugSkill) Debug.Log("Enter Tap Skill"); }
    protected virtual void EnterHoldSkill()    { if (debugSkill) Debug.Log("Enter Hold Skill"); }
    protected virtual void EnterSlowTapSkill() { if (debugSkill) Debug.Log("Enter SlowTap Skill"); }

    protected virtual void TapSkill()     { if (debugSkill) Debug.Log("Tap Skill");     EventManager.Player.OnPlayerSkill.Get("").Invoke(this, null); }
    protected virtual void HoldSkill()    { if (debugSkill) Debug.Log("Hold Skill");    EventManager.Player.OnPlayerSkill.Get("").Invoke(this, null); }
    protected virtual void SlowTapSkill() { if (debugSkill) Debug.Log("SlowTap Skill"); EventManager.Player.OnPlayerSkill.Get("").Invoke(this, null); }
    protected virtual void RepeatSkill()  { if (debugSkill) Debug.Log("Repeat Skill");  EventManager.Player.OnPlayerSkill.Get("").Invoke(this, null); }

    protected virtual void ExitTapSkill()     { if (debugSkill) Debug.Log("Exit Tap Skill"); }
    protected virtual void ExitHoldSkill()    { if (debugSkill) Debug.Log("Exit Hold Skill"); }
    protected virtual void ExitSlowTapSkill() { if (debugSkill) Debug.Log("Exit SlowTap Skill"); }

    protected virtual void CanceledTapSkill()     { if (debugSkill) Debug.Log("Canceled Tap Skill"); }
    protected virtual void CanceledHoldSkill()    { if (debugSkill) Debug.Log("Canceled Hold Skill"); }
    protected virtual void CanceledSlowTapSkill() { if (debugSkill) Debug.Log("Canceled SlowTap Skill"); }
    protected virtual void CanceledRepeatSkill()  { if (debugSkill) Debug.Log("Canceled Repeat Skill"); }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    //  ULTI
    // ═══════════════════════════════════════════════════════════════════
    #region Ulti

    protected virtual void StartedUlti(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapUlti)                       StartTapUlti();
        else if (IsHold(ctx)    && holdModeUlti == HoldMode.Hold)    StartHoldUlti();
        else if (IsSlowTap(ctx) && holdModeUlti == HoldMode.SlowTap) StartSlowTapUlti();
        else if (IsRepeat(ctx)  && holdModeUlti == HoldMode.Repeat)  StartRepeatUlti();
    }

    protected virtual void PerformedUlti(InputAction.CallbackContext ctx)
    {
        if (IsTap(ctx) && canTapUlti)
        { EnterTapUlti(); TapUlti(); ExitTapUlti(); }
        else if (IsHold(ctx) && holdModeUlti == HoldMode.Hold)
        { EnterHoldUlti(); HoldUlti(); ExitHoldUlti(); }
        else if (IsSlowTap(ctx) && holdModeUlti == HoldMode.SlowTap)
        { EnterSlowTapUlti(); SlowTapUlti(); ExitSlowTapUlti(); }
        else if (IsRepeat(ctx) && holdModeUlti == HoldMode.Repeat)
        { RepeatUlti(); }
    }

    protected virtual void CanceledUlti(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapUlti)                       CanceledTapUlti();
        else if (IsHold(ctx)    && holdModeUlti == HoldMode.Hold)    CanceledHoldUlti();
        else if (IsSlowTap(ctx) && holdModeUlti == HoldMode.SlowTap) CanceledSlowTapUlti();
        else if (IsRepeat(ctx)  && holdModeUlti == HoldMode.Repeat)  CanceledRepeatUlti();
    }

    protected virtual void StartTapUlti()     { if (debugUlti) Debug.Log("Start Tap Ulti"); }
    protected virtual void StartHoldUlti()    { if (debugUlti) Debug.Log("Start Hold Ulti"); }
    protected virtual void StartSlowTapUlti() { if (debugUlti) Debug.Log("Start SlowTap Ulti"); }
    protected virtual void StartRepeatUlti()  { if (debugUlti) Debug.Log("Start Repeat Ulti"); }

    protected virtual void EnterTapUlti()     { if (debugUlti) Debug.Log("Enter Tap Ulti"); }
    protected virtual void EnterHoldUlti()    { if (debugUlti) Debug.Log("Enter Hold Ulti"); }
    protected virtual void EnterSlowTapUlti() { if (debugUlti) Debug.Log("Enter SlowTap Ulti"); }

    protected virtual void TapUlti()     { if (debugUlti) Debug.Log("Tap Ulti");     EventManager.Player.OnPlayerUlti.Get("").Invoke(this, null); }
    protected virtual void HoldUlti()    { if (debugUlti) Debug.Log("Hold Ulti");    EventManager.Player.OnPlayerUlti.Get("").Invoke(this, null); }
    protected virtual void SlowTapUlti() { if (debugUlti) Debug.Log("SlowTap Ulti"); EventManager.Player.OnPlayerUlti.Get("").Invoke(this, null); }
    protected virtual void RepeatUlti()  { if (debugUlti) Debug.Log("Repeat Ulti");  EventManager.Player.OnPlayerUlti.Get("").Invoke(this, null); }

    protected virtual void ExitTapUlti()     { if (debugUlti) Debug.Log("Exit Tap Ulti"); }
    protected virtual void ExitHoldUlti()    { if (debugUlti) Debug.Log("Exit Hold Ulti"); }
    protected virtual void ExitSlowTapUlti() { if (debugUlti) Debug.Log("Exit SlowTap Ulti"); }

    protected virtual void CanceledTapUlti()     { if (debugUlti) Debug.Log("Canceled Tap Ulti"); }
    protected virtual void CanceledHoldUlti()    { if (debugUlti) Debug.Log("Canceled Hold Ulti"); }
    protected virtual void CanceledSlowTapUlti() { if (debugUlti) Debug.Log("Canceled SlowTap Ulti"); }
    protected virtual void CanceledRepeatUlti()  { if (debugUlti) Debug.Log("Canceled Repeat Ulti"); }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    //  DASH
    // ═══════════════════════════════════════════════════════════════════
    #region Dash

    protected virtual void StartedDash(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapDash)                       StartTapDash();
        else if (IsHold(ctx)    && holdModeDash == HoldMode.Hold)    StartHoldDash();
        else if (IsSlowTap(ctx) && holdModeDash == HoldMode.SlowTap) StartSlowTapDash();
        else if (IsRepeat(ctx)  && holdModeDash == HoldMode.Repeat)  StartRepeatDash();
    }

    protected virtual void PerformedDash(InputAction.CallbackContext ctx)
    {
        if (IsTap(ctx) && canTapDash)
        { EnterTapDash(); TapDash(); ExitTapDash(); }
        else if (IsHold(ctx) && holdModeDash == HoldMode.Hold)
        { EnterHoldDash(); HoldDash(); ExitHoldDash(); }
        else if (IsSlowTap(ctx) && holdModeDash == HoldMode.SlowTap)
        { EnterSlowTapDash(); SlowTapDash(); ExitSlowTapDash(); }
        else if (IsRepeat(ctx) && holdModeDash == HoldMode.Repeat)
        { RepeatDash(); }
    }

    protected virtual void CanceledDash(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapDash)                       CanceledTapDash();
        else if (IsHold(ctx)    && holdModeDash == HoldMode.Hold)    CanceledHoldDash();
        else if (IsSlowTap(ctx) && holdModeDash == HoldMode.SlowTap) CanceledSlowTapDash();
        else if (IsRepeat(ctx)  && holdModeDash == HoldMode.Repeat)  CanceledRepeatDash();
    }

    protected virtual void StartTapDash()     { if (debugDash) Debug.Log("Start Tap Dash"); }
    protected virtual void StartHoldDash()    { if (debugDash) Debug.Log("Start Hold Dash"); }
    protected virtual void StartSlowTapDash() { if (debugDash) Debug.Log("Start SlowTap Dash"); }
    protected virtual void StartRepeatDash()  { if (debugDash) Debug.Log("Start Repeat Dash"); }

    protected virtual void EnterTapDash()     { if (debugDash) Debug.Log("Enter Tap Dash"); }
    protected virtual void EnterHoldDash()    { if (debugDash) Debug.Log("Enter Hold Dash"); }
    protected virtual void EnterSlowTapDash() { if (debugDash) Debug.Log("Enter SlowTap Dash"); }

    protected virtual void TapDash()     { if (debugDash) Debug.Log("Tap Dash");     EventManager.Player.OnPlayerDash.Get("").Invoke(this, null); }
    protected virtual void HoldDash()    { if (debugDash) Debug.Log("Hold Dash");    EventManager.Player.OnPlayerDash.Get("").Invoke(this, null); }
    protected virtual void SlowTapDash() { if (debugDash) Debug.Log("SlowTap Dash"); EventManager.Player.OnPlayerDash.Get("").Invoke(this, null); }
    protected virtual void RepeatDash()  { if (debugDash) Debug.Log("Repeat Dash");  EventManager.Player.OnPlayerDash.Get("").Invoke(this, null); }

    protected virtual void ExitTapDash()     { if (debugDash) Debug.Log("Exit Tap Dash"); }
    protected virtual void ExitHoldDash()    { if (debugDash) Debug.Log("Exit Hold Dash"); }
    protected virtual void ExitSlowTapDash() { if (debugDash) Debug.Log("Exit SlowTap Dash"); }

    protected virtual void CanceledTapDash()     { if (debugDash) Debug.Log("Canceled Tap Dash"); }
    protected virtual void CanceledHoldDash()    { if (debugDash) Debug.Log("Canceled Hold Dash"); }
    protected virtual void CanceledSlowTapDash() { if (debugDash) Debug.Log("Canceled SlowTap Dash"); }
    protected virtual void CanceledRepeatDash()  { if (debugDash) Debug.Log("Canceled Repeat Dash"); }

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    //  BURST
    // ═══════════════════════════════════════════════════════════════════
    #region Burst

    protected virtual void StartedBurst(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapBurst)                       StartTapBurst();
        else if (IsHold(ctx)    && holdModeBurst == HoldMode.Hold)    StartHoldBurst();
        else if (IsSlowTap(ctx) && holdModeBurst == HoldMode.SlowTap) StartSlowTapBurst();
        else if (IsRepeat(ctx)  && holdModeBurst == HoldMode.Repeat)  StartRepeatBurst();
    }

    protected virtual void PerformedBurst(InputAction.CallbackContext ctx)
    {
        if (IsTap(ctx) && canTapBurst)
        { EnterTapBurst(); TapBurst(); ExitTapBurst(); }
        else if (IsHold(ctx) && holdModeBurst == HoldMode.Hold)
        { EnterHoldBurst(); HoldBurst(); ExitHoldBurst(); }
        else if (IsSlowTap(ctx) && holdModeBurst == HoldMode.SlowTap)
        { EnterSlowTapBurst(); SlowTapBurst(); ExitSlowTapBurst(); }
        else if (IsRepeat(ctx) && holdModeBurst == HoldMode.Repeat)
        { RepeatBurst(); }
    }

    protected virtual void CanceledBurst(InputAction.CallbackContext ctx)
    {
        if      (IsTap(ctx)     && canTapBurst)                       CanceledTapBurst();
        else if (IsHold(ctx)    && holdModeBurst == HoldMode.Hold)    CanceledHoldBurst();
        else if (IsSlowTap(ctx) && holdModeBurst == HoldMode.SlowTap) CanceledSlowTapBurst();
        else if (IsRepeat(ctx)  && holdModeBurst == HoldMode.Repeat)  CanceledRepeatBurst();
    }

    protected virtual void StartTapBurst()     { if (debugBurst) Debug.Log("Start Tap Burst"); }
    protected virtual void StartHoldBurst()    { if (debugBurst) Debug.Log("Start Hold Burst"); }
    protected virtual void StartSlowTapBurst() { if (debugBurst) Debug.Log("Start SlowTap Burst"); }
    protected virtual void StartRepeatBurst()  { if (debugBurst) Debug.Log("Start Repeat Burst"); }

    protected virtual void EnterTapBurst()     { if (debugBurst) Debug.Log("Enter Tap Burst"); }
    protected virtual void EnterHoldBurst()    { if (debugBurst) Debug.Log("Enter Hold Burst"); }
    protected virtual void EnterSlowTapBurst() { if (debugBurst) Debug.Log("Enter SlowTap Burst"); }

    protected virtual void TapBurst()     { if (debugBurst) Debug.Log("Tap Burst");     EventManager.Player.OnPlayerBurst.Get("").Invoke(this, null); }
    protected virtual void HoldBurst()    { if (debugBurst) Debug.Log("Hold Burst");    EventManager.Player.OnPlayerBurst.Get("").Invoke(this, null); }
    protected virtual void SlowTapBurst() { if (debugBurst) Debug.Log("SlowTap Burst"); EventManager.Player.OnPlayerBurst.Get("").Invoke(this, null); }
    protected virtual void RepeatBurst()  { if (debugBurst) Debug.Log("Repeat Burst");  EventManager.Player.OnPlayerBurst.Get("").Invoke(this, null); }

    protected virtual void ExitTapBurst()     { if (debugBurst) Debug.Log("Exit Tap Burst"); }
    protected virtual void ExitHoldBurst()    { if (debugBurst) Debug.Log("Exit Hold Burst"); }
    protected virtual void ExitSlowTapBurst() { if (debugBurst) Debug.Log("Exit SlowTap Burst"); }

    protected virtual void CanceledTapBurst()     { if (debugBurst) Debug.Log("Canceled Tap Burst"); }
    protected virtual void CanceledHoldBurst()    { if (debugBurst) Debug.Log("Canceled Hold Burst"); }
    protected virtual void CanceledSlowTapBurst() { if (debugBurst) Debug.Log("Canceled SlowTap Burst"); }
    protected virtual void CanceledRepeatBurst()  { if (debugBurst) Debug.Log("Canceled Repeat Burst"); }

    #endregion
}