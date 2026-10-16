using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;

// EN: Project_N's 3D player movement reads PlayerControls directly, as the original controller did.
// VI: PNPlayerMove 3D đọc trực tiếp PlayerControls như controller gốc.
[DisallowMultipleComponent]
public class PNPlayerMove : PNMove, IPNEntityResourceOwner
{
    [SerializeField, Range(0f, 1f)] private float minTimeScale = 1f;
    [SerializeField] private SpriteRenderer spriteRenderer;

    protected PlayerControls pc;
    protected Vector2 moveInput;
    private bool canInput = true;
    // Menu owns this gate independently of skill/death input locks.
    private bool menuInputBlocked;
    public bool MenuInputBlocked
    {
        get => menuInputBlocked;
        set { menuInputBlocked=value;if(value)SetMoveDirection(Vector2.zero); }
    }
    public virtual bool HasHeldMenuInput
    {
        get
        {
#if UNITY_5_3_OR_NEWER
            if(pc!=null)foreach(var control in pc.Controller.Move.controls)
                if(control.EvaluateMagnitude()>.2f)return true;
#endif
            return false;
        }
    }

    public virtual bool CanInput
    {
        get => canInput && !MenuInputBlocked && PNLocalPlayerInput.Allows(GetComponent<EveEntityLoader>());
        set
        {
            canInput = value;
            if (!value) SetMoveDirection(Vector2.zero);
        }
    }
    public virtual PlayerControls PC => pc;
    public bool BaseCanInput => canInput;

    protected override void ConfigureTimeScale(EveTimeScale timeScale) => timeScale.MinScale = minTimeScale;

    protected virtual void Awake()
        => EnsureInputInitialized();
    public void EnsureInputInitialized()
    {
        if (pc != null) return;
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        pc = new PlayerControls();
    }

    protected virtual void OnEnable()
    {
        pc?.Enable();
    }

    protected override void OnDisable()
    {
        pc?.Disable();
        base.OnDisable();
    }

    protected virtual void OnDestroy()
        => DisposeEntityResources();
    public void DisposeEntityResources() { pc?.Dispose();pc=null; }

    protected virtual void Update()
    {
        moveInput = CanInput && CanMove && pc != null ? pc.Controller.Move.ReadValue<Vector2>() : Vector2.zero;
        SetMoveDirection(moveInput);
        if (moveInput.x != 0f && CanFlip && MovementTimeScale > 0f)
        {
            FaceDirection(moveInput);
            if (spriteRenderer != null) spriteRenderer.flipX = FacingDirection < 0;
        }
    }


    // Skill selects aim; apply sprite facing immediately before it locks movement.
    public virtual void FaceSkillDirection(Vector2 direction)
    {
        if (!TryFaceSkill(direction)) return;
        if (spriteRenderer != null) spriteRenderer.flipX = FacingDirection < 0;
    }
    protected override void RefreshFacingVisual()
    { if(spriteRenderer!=null)spriteRenderer.flipX=FacingDirection<0; }
}
