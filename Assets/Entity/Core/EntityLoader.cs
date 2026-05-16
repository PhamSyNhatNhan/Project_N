using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Load JSON data từ Resources:
/// - Resources/Entity/Character/Name/Name  (layer = Player)
/// - Resources/Entity/Enemy/Name/Name      (layer = Enemy)
/// Phân phát cho Stat, Move, Skill trong Awake → Apply trong Start → tự Destroy
/// </summary>
public class EntityLoader : MonoBehaviour
{
    [SerializeField] private string entityName;

    private EntityData               entityData;
    private ILoadable<StatData>      stat;
    private ILoadable<MoveData>      move;
    private ILoadable<SkillListData> skill;

    // ── Awake — Load raw data ─────────────────────────────────────
    private void Awake()
    {
        if (string.IsNullOrEmpty(entityName))
        {
            Debug.LogError($"[EntityLoader] entityName chưa được set!", this);
            return;
        }

        string resourcePath = BuildResourcePath();
        if (string.IsNullOrEmpty(resourcePath)) return;

        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);
        if (textAsset == null)
        {
            Debug.LogError($"[EntityLoader] Không tìm thấy file: Resources/{resourcePath}.json", this);
            return;
        }

        entityData = JsonConvert.DeserializeObject<EntityData>(textAsset.text);
        if (entityData == null)
        {
            Debug.LogError($"[EntityLoader] Parse JSON thất bại: {resourcePath}", this);
            return;
        }

        stat  = GetComponent<ILoadable<StatData>>();
        move  = GetComponent<ILoadable<MoveData>>();
        skill = GetComponent<ILoadable<SkillListData>>();

        if (stat  != null && entityData.stat  != null) stat .LoadRawData(entityData.stat);
        if (move  != null && entityData.move  != null) move .LoadRawData(entityData.move);
        if (skill != null && entityData.skill != null) skill.LoadRawData(entityData.skill);
    }

    // ── Start — Apply data ────────────────────────────────────────
    private void Start()
    {
        if (entityData == null) return;

        if (stat  != null && entityData.stat  != null) stat .ApplyData();
        if (move  != null && entityData.move  != null) move .ApplyData();
        if (skill != null && entityData.skill != null) skill.ApplyData();

        Destroy(this);
    }

    // ── Build resource path ───────────────────────────────────────
    private string BuildResourcePath()
    {
        string layerName = LayerMask.LayerToName(gameObject.layer);

        string folder = layerName switch
        {
            "Player" => $"Entity/Character/{entityName}/Data",
            "Enemy"  => $"Entity/Enemy/{entityName}/Data",
            _        => ""
        };

        if (string.IsNullOrEmpty(folder))
        {
            Debug.LogError($"[EntityLoader] Layer '{layerName}' không được hỗ trợ. Cần là 'Player' hoặc 'Enemy'.", this);
            return "";
        }

        return $"{folder}/{entityName}";
    }
}