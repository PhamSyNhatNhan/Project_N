using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkillCd
{
    private SkillType skillType;
    private float baseSkillCd;
    private float curSkillCd;
    private float skillCdLeft;
    
    public void Update()
    {
        if (skillCdLeft > 0)
        {
            skillCdLeft -= Time.deltaTime;
            if(skillCdLeft < 0)
                skillCdLeft = 0;
        }
    }

    public SkillCd(SkillType skillType, float baseSkillCd)
    {
        this.skillType = skillType;
        this.baseSkillCd = baseSkillCd;
    }

    public float BaseSkillCd
    {
        get => baseSkillCd;
        set => baseSkillCd = value;
    }

    public float CurSkillCd
    {
        get => curSkillCd;
        set => curSkillCd = value;
    }

    public float SkillCdLeft
    {
        get => skillCdLeft;
        set => skillCdLeft = value;
    }

    public SkillType SkillType
    {
        get => skillType;
        set => skillType = value;
    }
}

