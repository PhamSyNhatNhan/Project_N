using UnityEngine;

// ════════════════════════════════════════════════════════════════
//  RogueBuffGroup — Component quản lý trên Prefab GO của 1 group
// ════════════════════════════════════════════════════════════════
public class RogueBuffGroup : MonoBehaviour
{
    // ── Inspector Setup ───────────────────────────────────────────
    [Header("Group Info")]
    [SerializeField] private BuffGroupId groupId;
    [SerializeField] private string      groupDisplayName;
    [SerializeField] private string      groupDescription;
    [SerializeField] private Sprite      groupIcon;

    [Header("Minor Buffs")]
    [SerializeField] private MinorBuff[] minorBuffs;

    [Header("Major Buff")]
    [SerializeField] private MajorBuff majorBuff;

    [Header("Unlock Condition")]
    [Tooltip("Số minor cần active để unlock major")]
    [SerializeField] private int requiredMinorToUnlockMajor = 3;

    // ── Runtime ───────────────────────────────────────────────────
    private bool[] _minorActive;
    private bool   _majorActive;

    // ── Events ────────────────────────────────────────────────────
    public System.Action<MinorBuff, int> OnMinorActivated;
    public System.Action<MajorBuff>      OnMajorUnlocked;

    // ── Properties ────────────────────────────────────────────────
    public BuffGroupId GroupId          => groupId;
    public string      GroupDisplayName => groupDisplayName;
    public string      GroupDescription => groupDescription;
    public int         MinorCount       => minorBuffs != null ? minorBuffs.Length : 0;
    public int         ActiveMinorCount { get; private set; }
    public bool        IsMajorUnlocked  => _majorActive;
    public int         RequiredMinor    => requiredMinorToUnlockMajor;

    public Sprite GroupIcon => groupIcon;

    public MinorBuff GetMinor(int index) => index >= 0 && index < MinorCount ? minorBuffs[index] : null;
    public MajorBuff Major               => majorBuff;

    // ── Awake ─────────────────────────────────────────────────────
    private void Awake()
    {
        _minorActive = new bool[MinorCount];
        EnsureAllDisabled();
    }

    // ── Initialize — restore từ save ─────────────────────────────
    public void Initialize(bool[] minorStates)
    {
        if (minorStates == null || minorStates.Length != MinorCount)
        {
            Debug.LogError($"[RogueBuffGroup:{groupId}] minorStates.Length ({minorStates?.Length}) không khớp MinorCount ({MinorCount})");
            return;
        }

        for (int i = 0; i < MinorCount; i++)
        {
            if (minorStates[i])
                ActivateMinorImmediate(i);
        }

        TryUnlockMajor();
    }

    // ── ActivateMinor ─────────────────────────────────────────────
    public void ActivateMinor(int index)
    {
        if (index < 0 || index >= MinorCount)
        {
            Debug.LogError($"[RogueBuffGroup:{groupId}] Index {index} ngoài range 0-{MinorCount - 1}");
            return;
        }

        if (_minorActive[index])
        {
            Debug.LogWarning($"[RogueBuffGroup:{groupId}] Minor[{index}] đã active rồi");
            return;
        }

        ActivateMinorImmediate(index);
        TryUnlockMajor();
    }

    // ── Deactivate ────────────────────────────────────────────────
    public void DeactivateMinor(int index)
    {
        if (index < 0 || index >= MinorCount || !_minorActive[index]) return;

        var buff = minorBuffs[index];
        if (buff == null) return;

        buff.Remove();
        buff.enabled        = false;
        _minorActive[index] = false;
        ActiveMinorCount    = Mathf.Max(0, ActiveMinorCount - 1);
    }

    public void DeactivateAll()
    {
        for (int i = 0; i < MinorCount; i++)
            DeactivateMinor(i);

        if (_majorActive)
        {
            majorBuff?.Remove();
            if (majorBuff != null) majorBuff.enabled = false;
            _majorActive = false;
        }

        ActiveMinorCount = 0;
    }

    // ── Query ─────────────────────────────────────────────────────
    public bool IsMinorActive(int index) => index >= 0 && index < MinorCount && _minorActive[index];

    public bool[] GetMinorStates()
    {
        var states = new bool[MinorCount];
        for (int i = 0; i < MinorCount; i++)
            states[i] = _minorActive[i];
        return states;
    }

    // ── Internal ──────────────────────────────────────────────────
    private void ActivateMinorImmediate(int index)
    {
        var buff = minorBuffs[index];
        if (buff == null)
        {
            Debug.LogWarning($"[RogueBuffGroup:{groupId}] Minor[{index}] chưa assign trên Inspector");
            return;
        }

        buff.enabled        = true;
        buff.Apply();
        _minorActive[index] = true;
        ActiveMinorCount++;
        OnMinorActivated?.Invoke(buff, index);
    }

    private void TryUnlockMajor()
    {
        if (_majorActive) return;
        if (ActiveMinorCount < requiredMinorToUnlockMajor) return;
        ActivateMajorImmediate();
    }

    private void ActivateMajorImmediate()
    {
        if (majorBuff == null)
        {
            Debug.LogWarning($"[RogueBuffGroup:{groupId}] MajorBuff chưa assign trên Inspector");
            return;
        }

        majorBuff.enabled = true;
        majorBuff.Apply();
        _majorActive = true;
        OnMajorUnlocked?.Invoke(majorBuff);
        Debug.Log($"[RogueBuffGroup:{groupId}] '{majorBuff.DisplayName}' unlocked!");
    }

    private void EnsureAllDisabled()
    {
        if (minorBuffs != null)
            foreach (var b in minorBuffs)
                if (b != null) b.enabled = false;

        if (majorBuff != null) majorBuff.enabled = false;
    }
}