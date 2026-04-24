using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
public class PlayerController : Move
{
    protected PlayerControls pc;
    protected Vector2 _Move;

    protected override void Awake()
    {
        base.Awake();
        pc = new PlayerControls();
    }

    protected void OnEnable()
    {
        pc.Enable();
    }

    protected void OnDisable()
    {
        pc.Disable();
    }

    protected override void Update()
    {
        base.Update();
        CheckInput();
        CheckFlip();
    }

    protected override void MovementSetUp()
    {
        if (_Move != Vector2.zero)
        {
            rb.linearVelocity = new Vector2(
                _Move.x * curMoveSpeed * FixedDeltaTime,
                _Move.y * curMoveSpeed * FixedDeltaTime
            );
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    protected virtual void CheckInput()
    {
        _Move = pc.Controller.Move.ReadValue<Vector2>();
    }

    protected virtual void CheckFlip()
    {
        if (canFlip && flipDirect > 0 && _Move.x < 0.0f)
            Flipping();
        else if (canFlip && flipDirect < 0 && _Move.x > 0.0f)
            Flipping();
    }

    public virtual void Flipping(Transform enemy)
    {
        if (transform.position.x < enemy.position.x && flipDirect == -1)
            Flipping();
        else if (transform.position.x > enemy.position.x && flipDirect == 1)
            Flipping();
    }

    public PlayerControls PC
    {
        get => pc;
        private set => pc = value;
    }
}