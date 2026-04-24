using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;

public class GuraSkill : PlayerSkill
{
    protected Gura guraStat;
    protected GuraController guraController;
    
    [Header("Generic")] 
    private Hitbox hitbox;

    [Header("Attack")] 
    [SerializeField] private float attackDelay = 0.1f;
    private int numberAttack = 0;
    private Coroutine coroutineResetAttack;
    [SerializeField] private Transform transformNorAttack;
    [SerializeField] private GameObject prefabAttack1;
    [SerializeField] private GameObject prefabAttack2;
    [SerializeField] private GameObject prefabAttack3;
     
    //[Header("Skill")]
    
    [Header("Ulti")]
    [SerializeField] private GameObject prefabUltiEnd;
    [SerializeField] private Transform transformUltiEnd;
    private bool isDive = false;
    private Coroutine CrUltiActive;
    
    //[Header("Dash")]


    private void OnEnable()
    {
        //EventManager.Player.OnAttackEnd.Get(guraStat.NameCharacter).AddListener((component, data) => OnEndNormalAttack());
        //EventManager.Player.OnPlayerAttackSpeedChange.Get(guraStat.NameCharacter).AddListener((component, data) => OnAttackSpeedChange());
        //EventManager.Player.PlayerFlipCall.Get(guraStat.NameCharacter).AddListener((component) => FlipCall());
        //EventManager.Player.OnMoveToEnd.Get(guraStat.NameCharacter).AddListener((component, data) => EndDash());

    }

    private void OnDisable()
    {
        //EventManager.Player.OnAttackEnd.Get().RemoveListener((component, data) => OnEndNormalAttack());
        //EventManager.Player.OnPlayerAttackSpeedChange.Get(guraStat.NameCharacter).RemoveListener((component, data) => OnAttackSpeedChange());
        //EventManager.Player.PlayerFlipCall.Get(guraStat.NameCharacter).RemoveListener((component) => FlipCall());
        //EventManager.Player.OnMoveToEnd.Get(guraStat.NameCharacter).RemoveListener((component, data) => EndDash());
    }

    protected override void AwakeSetUp()
    {
        guraStat = GetComponent<Gura>();
        guraController = GetComponent<GuraController>();
        hitbox = GetComponent<Hitbox>();
    }
    
    /*
    protected override void StartSetUp()
    {
        SetUpObject();
    }

    
    private void SetUpObject()
    {
        //Attack
        GameObject dmpAttackNor1 = Instantiate(prefabAttack1, transformNorAttack);
        skillObjectsMap.Add("Attack1", dmpAttackNor1);
        GameObject dmpAttackNor2 = Instantiate(prefabAttack2, transformNorAttack);
        skillObjectsMap.Add("Attack2", dmpAttackNor2);
        GameObject dmpAttackNor3 = Instantiate(prefabAttack3, transformNorAttack);
        skillObjectsMap.Add("Attack3", dmpAttackNor3);
        
        
        //Ulti
        GameObject dmpUltiEnd = Instantiate(prefabUltiEnd, transformUltiEnd);
        skillObjectsMap.Add("UltiEnd",dmpUltiEnd);
        
        //Ulti

        foreach (var skillObject in skillObjectsMap)
        {
            skillObject.Value.GetComponent<ProjectileObject>().SetUp(guraStat.NameCharacter);
            skillObject.Value.SetActive(false);
        }
    }
    
    private void OnAttackSpeedChange()
    {
        
    }

    private void FlipCall()
    {
        List<GameObject> Enemy = hitbox.detectObject(enemyLayer);
        if (Enemy.Count != 0)
        {
            guraController.Flipping(Enemy[0].transform);
        }
    }

    protected override void TapAttack()
    {
        if (canInput)
        {
            if (canAttack && !isAttack && !isSkill && !isUlti && !isDash)
            {
                if (isDive)
                {
                    
                }
                else if (skillCd["Attack"].SkillCdLeft == 0)
                {
                    if (EventManager.Player.OnPlayerAttack != null)
                    {
                        EventManager.Player.OnPlayerAttack.Get(guraStat.NameCharacter).Invoke(this, null);
                    }
                    
                    canAttack = false;
                    guraController.CanFlip = false;
                    
                    if (coroutineResetAttack == null)
                    {
                        coroutineResetAttack = StartCoroutine(IEResetAttack());
                    }
                    else
                    {
                        StopCoroutine(coroutineResetAttack);
                        coroutineResetAttack = StartCoroutine(IEResetAttack());
                    }

                    string attackName = "Attack" + (numberAttack + 1).ToString();
                    
                    skillObjectsMap[attackName].GetComponent<ProjectileObject>().SetUp(DamageType.Magic, new List<float>(){1.0f}, guraStat.CurCritRate, guraStat.CurCritDamage, guraStat.CurAttackSpeed);
                    skillObjectsMap[attackName].SetActive(true);
                    
                    numberAttack += 1;
                    if (numberAttack > 2) numberAttack = 0;
                }
            }
        }
    }

    private void OnEndNormalAttack()
    {
        canAttack = true;
        skillCdMap["Attack"].SkillCdLeft = skillCdMap["Attack"].CurSkillCd;
        guraController.CanFlip = true;
    }

    private IEnumerator IEResetAttack()
    {
        yield return new WaitForSeconds(1.0f);

        numberAttack = 0;
    }

    protected override void TapUlti()
    {
        if (canInput)
        {
            if (canUlti && skillCdMap["Ulti"].SkillCdLeft == 0 && !isAttack && !isSkill && !isUlti && !isDash)
            {
                if (!isDive)
                {
                    if (EventManager.Player.OnPlayerUlti != null)
                    {
                        EventManager.Player.OnPlayerUlti.Get(guraStat.NameCharacter).Invoke(this, null);
                    }
                    
                    isDive = true;
                    guraStat.CanDamge = false;
                    
                    if (CrUltiActive == null)
                    {
                        CrUltiActive = StartCoroutine(IEEndUlti());
                    }
                    else
                    {
                        StopCoroutine(CrUltiActive);
                        CrUltiActive = StartCoroutine(IEEndUlti());
                    }
                }
                else
                {
                    StopCoroutine(CrUltiActive);

                    isUlti = true;
                    
                    isDive = false;
                    guraController.CanMove = false;
                    skillObjectsMap["UltiEnd"].GetComponent<ProjectileObject>().SetUp(DamageType.Magic, new List<float>(){1.0f}, guraStat.CurCritRate, guraStat.CurCritDamage);
                    skillObjectsMap["UltiEnd"].SetActive(true);
                    
                    StartCoroutine(UltiEndAnimation(skillObjectsMap["UltiEnd"].GetComponent<Animator>()
                        .GetCurrentAnimatorStateInfo(0).length * 0.75f));
                    
                    skillCdMap["Ulti"].SkillCdLeft = skillCdMap["Ulti"].CurSkillCd;
                }
            }
        }
    }

    private IEnumerator IEEndUlti()
    {
        yield return new WaitForSeconds(5.0f);
        
        TapUlti();
    }

    private IEnumerator UltiEndAnimation(float time)
    {
        yield return new WaitForSeconds(time);
        isUlti = false;
        guraStat.CanDamge = true;
        guraController.CanMove = true;
    }
    
    protected override void TapDash()
    {
        if (canInput)
        {
            if (canDash && skillCdMap["Dash"].SkillCdLeft == 0 && !isAttack && !isSkill && !isUlti && !isDash)
            {
                guraController.MoveTo(500.0f, 0.15f);
                isDash = true;
            }
        }
    }

    private void EndDash()
    {
        isDash = false;
    }

    protected override void TapSkill()
    {
        //Debug.Log("Skill Active");
        //EventManager.Enviroment.PerBoolInteractiveEvent.Get("GateMainHall").Invoke(this, true);
    }
    

    private void OnDrawGizmos()
    {

    }

    public bool IsDive
    {
        get => isDive;
        set => isDive = value;
    }
    */
}
