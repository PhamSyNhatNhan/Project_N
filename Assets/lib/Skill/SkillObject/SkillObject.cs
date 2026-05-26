using UnityEngine;

public class SkillObject : MonoBehaviour
{
    [SerializeField] protected ObjectSkillType objectSkillType;
    protected Stat  stat;
    protected float iFrameDuration = 0.05f;

    protected virtual void Awake()
    {
        foreach (var col in GetComponents<Collider2D>())
            col.isTrigger = true;
    }

    public ObjectSkillType ObjectSkillType
    {
        get => objectSkillType;
        set => objectSkillType = value;
    }

    public float IFrameDuration
    {
        get => iFrameDuration;
        set => iFrameDuration = value;
    }
}