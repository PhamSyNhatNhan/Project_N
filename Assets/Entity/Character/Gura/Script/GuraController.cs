using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GuraController : PlayerController
{
    private Animator amt;
    private GuraSkill gs;

    protected override void Awake()
    {
        base.Awake();
        amt = GetComponent<Animator>();
        gs = GetComponent<GuraSkill>();
    }
    

    protected override void FixedUpdate()
    {
        base.FixedUpdate();
        AnimatorControl();
    }
    


    private void AnimatorControl()
    {
        amt.SetBool("isMove", isMove);
        //amt.SetBool("isDive", gs.IsDive);
    }
}