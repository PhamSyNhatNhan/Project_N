using UnityEngine;

public class SkillObject : MonoBehaviour
{
    [SerializeField] protected ObjectSkillType objectSkillType;
    protected Stat stat;

    
    public ObjectSkillType ObjectSkillType
    {
        get => objectSkillType;
        set => objectSkillType = value;
    }
}
