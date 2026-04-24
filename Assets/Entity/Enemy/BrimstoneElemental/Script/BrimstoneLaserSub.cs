using System;
using UnityEngine;

public class BrimstoneLaserSub : MonoBehaviour
{
    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        animator.SetBool("isHold", false);
        animator.SetBool("isEnd", false);
    }

    public void OnLaserHold()
    {
        animator.SetBool("isHold", true);
        animator.SetBool("isEnd", false);
    }
    
    public void OnLaserEnd()
    {
        animator.SetBool("isHold", false);
        animator.SetBool("isEnd", true);
    }
    
    public void OnLaserEndEnd()
    {
        gameObject.SetActive(false);
    }
}
