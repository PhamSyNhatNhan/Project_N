using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestSkill : PlayerSkill
{
    protected override void TapSkill()
    {
        Debug.Log("Skill");
    }

    protected override void TapAttack()
    {
        Debug.Log("Attack");
    }

    protected override void TapUlti()
    {
        Debug.Log("Burst");
    }

    protected override void TapDash()
    {
        Debug.Log("Dash");
    }
}
