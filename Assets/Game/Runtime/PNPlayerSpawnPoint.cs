#if UNITY_5_3_OR_NEWER
using UnityEngine;
[DefaultExecutionOrder(-10000)]
public sealed class PNPlayerSpawnPoint : MonoBehaviour
{
    public GameObject Prefab;
    public Collider Ground;
    public string SceneKey="project_n:ermode";
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(SceneKey)||SceneKey.Length>128||Prefab==null||Ground==null||Prefab.GetComponent<EveEngine.EveEntityLoader>()==null||Prefab.GetComponent<EveEngine.EveEntityIdentity>()==null)
            throw new System.InvalidOperationException("Spawn point requires a stable scene key, entity prefab and scene ground.");
    }
}
#endif
