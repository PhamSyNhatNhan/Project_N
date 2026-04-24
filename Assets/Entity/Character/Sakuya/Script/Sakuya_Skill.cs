using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SakuyaSkill : PlayerSkill
{
    protected Sakuya sakuyaStat;
    protected SakuyaController sakuyaController;
    private RogueBuffManager buffManager;

    
    [Header("Generic")] 
    private Hitbox hitbox;

    [Header("Attack")] 
    [SerializeField] private float attackDelay;
    [SerializeField] private GameObject Prefab_Sakuya_Knife;
    private EasyPoolingList testList = new EasyPoolingList();
    private EasyPoolingList testList2 = new EasyPoolingList();
    
    //[Header("Skill")]
    
    //[Header("Ulti")]
    
    //[Header("Dash")]
    
    private void OnEnable()
    {
        EventManager.Player.OnMoveToEnd.Get(sakuyaStat.NameCharacter).AddListener((component, data) => EndDash());
    }

    private void OnDisable()
    {
        EventManager.Player.OnMoveToEnd.Get(sakuyaStat.NameCharacter).AddListener((component, data) => EndDash());
    }
    
    protected override void AwakeSetUp()
    {
        sakuyaStat = GetComponent<Sakuya>();
        sakuyaController = GetComponent<SakuyaController>();
        hitbox = GetComponent<Hitbox>();
        buffManager = GetComponent<RogueBuffManager>();

    }

    protected override void StartSetUp()
    {
        SetUpObject();
        EasyAttackRepeat(0.4f, 0.2f);
        EasyAttackSlowTapDuration(1.5f);
    }

    private void SetUpObject()
    {
        //Attack
        testList.SetPrefab(Prefab_Sakuya_Knife);
        testList2.SetPrefab(Prefab_Sakuya_Knife);
        
    }
    
    private void OnAttackSpeedChange()
    {
        
    }
    
    private void FlipCall()
    {
        List<GameObject> Enemy = hitbox.detectObject(enemyLayer);
        if (Enemy.Count != 0)
        {
            sakuyaController.Flipping(Enemy[0].transform);
        }
    }

    protected override void TapAttack()
    {
        if (!canInput) return;
        if (!IsReady("NormalKnife") || isAttack || isSkill || isUlti || isDash) return;

        sakuyaController.CanFlip = false;
        EventManager.Player.OnPlayerAttack.Get(sakuyaStat.NameCharacter).Invoke(this, null);
        isAttack = true;

        var enemies = hitbox.detectObject(enemyLayer);
        bool isFacingRight = enemies != null && enemies.Count > 0
            ? enemies[0].transform.position.x > transform.position.x
            : sakuyaController.FlipDirect == 1;

        var data = skillData["NormalKnife"];

        // Knife thường
        GameObject knife1 = testList.GetGameObject();
        knife1.transform.position = transform.position;
        knife1.transform.rotation = Quaternion.Euler(0f, isFacingRight ? 0f : 180f, 0f);
        knife1.GetComponent<SakuyaKnife>().FlipDirect = isFacingRight ? 1 : -1;
        knife1.GetComponent<SakuyaKnife>().SetUp(DamageType.Physical, data.damage, 50.0f, 50.0f);
        knife1.SetActive(true);
        knife1.transform.parent = null;

        // Knife nhỏ
        GameObject knife2 = testList2.GetGameObject();
        knife2.transform.position   = transform.position + new Vector3(0f, 0.3f, 0f);
        knife2.transform.localScale = Vector3.one * 0.5f;
        knife2.transform.rotation   = Quaternion.Euler(0f, isFacingRight ? 0f : 180f, 0f);
        knife2.GetComponent<SakuyaKnife>().FlipDirect = isFacingRight ? 1 : -1;
        knife2.GetComponent<SakuyaKnife>().SetUp(DamageType.True, data.damage, 50.0f, 50.0f);
        knife2.GetComponent<TimeScale>()?.AddModifier("small_knife", 0.5f);
        knife2.SetActive(true);
        knife2.transform.parent = null;

        /*
        // Apply tất cả effect lên enemy
        if (enemies != null)
        {
            foreach (var enemy in enemies)
            {
                var em = enemy.GetComponent<StatusEffectManager>();
                if (em == null) continue;

                em.AddStack(new BurnEffect(),   sakuyaStat, duration: 5f, tickInterval: 0.5f, maxStacks: 5);
                em.AddStack(new PoisonEffect(), sakuyaStat, duration: 5f, tickInterval: 1.0f, maxStacks: 5);
                em.AddStack(new BleedEffect(),  sakuyaStat, duration: 5f, tickInterval: 0.8f, maxStacks: 3);

                em.Apply(new SlowEffect(), sakuyaStat, duration: 3f);
                em.Apply(new StunEffect(), sakuyaStat, duration: 2f);

                em.AddMilestoneStack(new FreezeEffect(), sakuyaStat, threshold: 5, triggerDuration: 2f, stackDuration: 8f);
                em.AddMilestoneStack(new ShockEffect(),  sakuyaStat, threshold: 3, triggerDuration: 1f, stackDuration: 6f);
            }
        }
        */

        skillCd["NormalKnife"].Use();
        StartCoroutine(EndAttack());
    }

    protected override void HoldAttack()
    {
        if (canInput)
        {
            if (canDash && skillCd["NormalKnife"].IsReady && !isAttack && !isSkill && !isUlti && !isDash)
            {
                buffManager.ActivateMinor(BuffGroupId.Warrior, 1);
                buffManager.ActivateMinor(BuffGroupId.Warrior, 2);
                buffManager.ActivateMinor(BuffGroupId.Warrior, 4);
            }
        }
    }

    protected override void SlowTapAttack()
    {
        if (canInput)
        {
            if (canDash && skillCd["NormalKnife"].IsReady && !isAttack && !isSkill && !isUlti && !isDash)
            {
                sakuyaController.CanFlip = false;
                EventManager.Player.OnPlayerAttack.Get(sakuyaStat.NameCharacter).Invoke(this, null);
                isAttack = true;

                for (int i = 0; i < 3; i++)
                {
                    GameObject knife = testList.GetGameObject();
                    knife.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                    knife.GetComponent<SakuyaKnife>().FlipDirect = 1;
                    knife.transform.position = transform.position;
                    if (sakuyaController.FlipDirect != 1)
                    {
                        knife.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                        knife.GetComponent<SakuyaKnife>().FlipDirect = -1;
                    }
                    knife.SetActive(true);
                    knife.transform.parent = null;
                }

                StartCoroutine(EndAttack());
            }
        }
    }

    protected override void RepeatAttack()
    {
        if (canInput)
        {
            if (canDash && skillCd["NormalKnife"].IsReady && !isAttack && !isSkill && !isUlti && !isDash)
            {
                sakuyaController.CanFlip = false;
                EventManager.Player.OnPlayerAttack.Get(sakuyaStat.NameCharacter).Invoke(this, null);
                isAttack = true;

                for (int i = 0; i < 4; i++)
                {
                    GameObject knife = testList.GetGameObject();
                    knife.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                    knife.GetComponent<SakuyaKnife>().FlipDirect = 1;
                    knife.transform.position = transform.position;
                    if (sakuyaController.FlipDirect != 1)
                    {
                        knife.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                        knife.GetComponent<SakuyaKnife>().FlipDirect = -1;
                    }
                    knife.SetActive(true);
                    knife.transform.parent = null;
                }

                StartCoroutine(EndAttack());
            }
        }
    }

    private IEnumerator EndAttack()
    {
        yield return new WaitForSeconds(0.1f);
        
        isAttack = false;
        sakuyaController.CanFlip = true;
    }

    protected override void TapDash()
    {
        if (canInput)
        {
            if (canDash && skillCd["Dash"].IsReady && !isAttack && !isSkill && !isUlti && !isDash)
            {
                Debug.Log("Dash Tap");
                sakuyaController.MoveTo(500.0f, 0.15f);
                isDash = true;
                
                
            }
        }
    }
    
    private void EndDash()
    {
        isDash = false;
    }
}
