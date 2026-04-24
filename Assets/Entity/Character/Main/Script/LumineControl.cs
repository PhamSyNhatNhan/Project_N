using UnityEngine;

public class LumineControl : MonoBehaviour
{
    private Rigidbody2D rb;
    private PlayerControls pc;
    private Animator amt;

    [Header("Movement")]
    [SerializeField] protected float speed = 200.0f;
    private Vector2 _Move;
    private int flipDirect = 1;
    private bool canMove = true;
    private bool canFlip = true;
    private bool isMove = false;
    

    protected virtual void Awake()
    {
        pc = new PlayerControls();
        rb = GetComponent<Rigidbody2D>();
        amt = GetComponent<Animator>();
    }

    protected void OnEnable()
    {
        pc.Enable();
    }

    protected void OnDisable()
    {
        pc.Disable();
    }

    protected virtual void Start()
    {
    }
    
    protected virtual void Update()
    {
        CheckInput();
        CheckFlip();
        AnimatorControl();
    }

    protected virtual void FixedUpdate()
    {
        Movement();
        FixedUpdateSetUp();
    }
    protected virtual void FixedUpdateSetUp(){}

    

    protected virtual void Movement()
    {
        if (canMove)
        {
            rb.linearVelocity = new Vector2(_Move.x * speed * Time.deltaTime, _Move.y * speed * Time.deltaTime);
        }
        else 
        {
            rb.linearVelocity = Vector2.zero;
        }
    }
    

    public void stopMovement()
    {
        rb.linearVelocity = Vector2.zero;
    }

    protected virtual void CheckInput()
    {
        _Move = pc.Controller.Move.ReadValue<Vector2>();
        
        if(_Move != Vector2.zero)
        {
            isMove = true;
        }
        else
        {
            isMove = false;
        }
    }

    protected virtual void CheckFlip()
    {
        if (canFlip && flipDirect > 0 && _Move.x < 0.0f)
        {
            Flipping();
        }
        else if (canFlip && flipDirect < 0 && _Move.x > 0.0f)
        {
            Flipping();
        }
        
    }
    
    protected virtual void Flipping()
    {
        flipDirect *= -1;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }
    
    public virtual void Flipping(Transform enemy)
    {
        if (transform.position.x < enemy.position.x && flipDirect == -1)
        {
            Flipping();
        }
        else if (transform.position.x > enemy.position.x && flipDirect == 1)
        {
            Flipping();
        }
    }
    
    private void AnimatorControl()
    {
        amt.SetBool("isMove", isMove);
    }

    public PlayerControls PC
    {
        get => pc;
        private set => pc = value;
    }

    public bool CanMove
    {
        get => canMove;
        set => canMove = value;
    }

    public bool CanFlip
    {
        get => canFlip;
        set => canFlip = value;
    }

    public Rigidbody2D Rb
    {
        get => rb;
        set => rb = value;
    }
}
