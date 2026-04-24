using System.IO;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Load JSON data từ:
/// - Assets/Entity/Character/Name/Name.json  (layer = Player)
/// - Assets/Entity/Enemy/Name/Name.json      (layer = Enemy)
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

        string path = BuildPath();

        if (!File.Exists(path))
        {
            Debug.LogError($"[EntityLoader] Không tìm thấy file: {path}", this);
            return;
        }

        string json = File.ReadAllText(path);
        entityData  = JsonConvert.DeserializeObject<EntityData>(json);

        if (entityData == null)
        {
            Debug.LogError($"[EntityLoader] Parse JSON thất bại: {path}", this);
            return;
        }

        // Thu thập components qua interface — tự tìm đúng class (Player hay Enemy)
        stat  = GetComponent<ILoadable<StatData>>();
        move  = GetComponent<ILoadable<MoveData>>();
        skill = GetComponent<ILoadable<SkillListData>>();

        // Phase 1 — Load raw data
        if (stat  != null && entityData.stat  != null) stat .LoadRawData(entityData.stat);
        if (move  != null && entityData.move  != null) move .LoadRawData(entityData.move);
        if (skill != null && entityData.skill != null) skill.LoadRawData(entityData.skill);
    }

    // ── Start — Apply data ────────────────────────────────────────
    private void Start()
    {
        if (entityData == null) return;

        // Phase 2 — Apply (tất cả Awake đã xong)
        if (stat  != null && entityData.stat  != null) stat .ApplyData();
        if (move  != null && entityData.move  != null) move .ApplyData();
        if (skill != null && entityData.skill != null) skill.ApplyData();

        // Load xong, không cần nữa
        Destroy(this);
    }

    // ── Build path — dựa vào layer ────────────────────────────────
    private string BuildPath()
    {
        string layerName = LayerMask.LayerToName(gameObject.layer);

        string folder = layerName switch
        {
            "Player" => Path.Combine(Application.dataPath, "Entity", "Character", entityName, "Data"),
            "Enemy"  => Path.Combine(Application.dataPath, "Entity", "Enemy",     entityName, "Data"),
            _        => ""
        };

        if (string.IsNullOrEmpty(folder))
        {
            Debug.LogError($"[EntityLoader] Layer '{layerName}' không được hỗ trợ. Cần là 'Player' hoặc 'Enemy'.", this);
            return "";
        }

        return Path.Combine(folder, $"{entityName}.json");
    }
}