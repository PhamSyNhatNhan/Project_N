using UnityEngine;

public class Dael_Controller : PlayerController
{
    //private Animator amt;
    
    protected override void Awake()
    {
        base.Awake();
        //amt = GetComponent<Animator>();
    }
    
    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        //AnimatorControl();
    }

    public override void ApplyData()
    {
        base.ApplyData();
        
    }

    /*
    private void AnimatorControl()
    {
        amt.SetBool("isMove", isMove);
    }
    */
}
