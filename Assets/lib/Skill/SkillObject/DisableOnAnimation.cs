using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DisableOnAnimation : MonoBehaviour
{
    private Animator amt;
    
    private void OnEnable()
    {
        if (amt == null)
            amt = GetComponent<Animator>();
        if (amt != null)
            StartCoroutine(DisableAfterAnimation());
    }
    
    private IEnumerator DisableAfterAnimation()
    {
        AnimatorStateInfo stateInfo = amt.GetCurrentAnimatorStateInfo(0);
        float adjustedTime = stateInfo.length / amt.speed;
        yield return new WaitForSeconds(adjustedTime);
        gameObject.SetActive(false);
    }
}
