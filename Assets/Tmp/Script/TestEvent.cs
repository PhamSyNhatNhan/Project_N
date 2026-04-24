using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestEvent : MonoBehaviour
{
    private void OnEnable()
    {
        EventManager.Player.OnPlayerAttack.Get("").AddListener((component, data) => TestEventFunc("Attack"));
        EventManager.Player.OnPlayerAttackSpeedChange.Get("").AddListener((component, data) => ChangeAttackSpeed(float.Parse(data.ToString())));
        EventManager.Player.OnAttackEnd.Get().AddListener((component, data) => AttackEnd());
    }
    

    private void OnDisable()
    {
        EventManager.Player.OnPlayerAttack.Get("").RemoveListener((component, data) => TestEventFunc("Attack"));
        EventManager.Player.OnPlayerAttackSpeedChange.Get("").RemoveListener((component, data) => ChangeAttackSpeed(float.Parse(data.ToString())));
        EventManager.Player.OnAttackEnd.Get().RemoveListener((component, data) => AttackEnd());
    }
    private void ChangeAttackSpeed(float speed)
    {
        //Debug.Log(speed);
    }
    
    private int count = 0;
    
    private void AttackEnd()
    {
        //Debug.Log("Attack End" + count);
        count += 1;
    }


    private void TestEventFunc(string eventName)
    {
        //Debug.Log(eventName);
    }
}
