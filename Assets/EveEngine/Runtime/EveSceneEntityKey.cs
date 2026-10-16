using System;
using UnityEngine;
namespace EveEngine
{
    [DisallowMultipleComponent]
    public sealed class EveSceneEntityKey : MonoBehaviour
    {
        [SerializeField] private string sceneKey;
        [SerializeField] private string entityKey;
        public string SceneKey=>sceneKey;
        public string EntityKey=>entityKey;
        public void Configure(string scene,string entity)
        { if(string.IsNullOrWhiteSpace(scene)||string.IsNullOrWhiteSpace(entity))throw new ArgumentException("Stable scene/entity keys required.");sceneKey=scene;entityKey=entity; }
        public void Validate()
        { if(string.IsNullOrWhiteSpace(sceneKey)||string.IsNullOrWhiteSpace(entityKey)||sceneKey.Length>128||entityKey.Length>128)throw new InvalidOperationException("Missing/invalid authored SceneKey+EntityKey"); }
        public static void ValidateSet(System.Collections.Generic.IEnumerable<EveSceneEntityKey> keys)
        {
            if(keys==null)throw new ArgumentNullException(nameof(keys));
            var seen=new System.Collections.Generic.HashSet<(string,string)>();
            foreach(var key in keys)
            {if(key==null)throw new InvalidOperationException("Missing authored entity metadata.");key.Validate();if(!seen.Add((key.SceneKey,key.EntityKey)))throw new InvalidOperationException("Duplicate SceneKey+EntityKey.");}
        }
    }
}
