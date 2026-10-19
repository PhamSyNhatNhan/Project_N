#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Newtonsoft.Json.Linq;

// Generates only disposable assets in a clone. Never call in the authored working project.
public static class PNWorldAssetPackBuilder
{
    public static void Build()
    {
        string output=Environment.GetCommandLineArgs().First(a=>a.StartsWith("--pn-world-pack=",StringComparison.Ordinal)).Substring("--pn-world-pack=".Length);
        try{BuildTo(output);EditorApplication.Exit(0);}catch(Exception e){File.WriteAllText(Path.Combine(output,"BundleFailure.txt"),e.ToString());EditorApplication.Exit(1);}
    }
    public static void BuildTo(string output)
    {
        if(!Application.dataPath.Replace('\\','/').Contains("/.utmp/"))throw new InvalidOperationException("Disposable .utmp clone required.");
        Directory.CreateDirectory(output);const string folder="Assets/WorldAssetPackFixture";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var shader=Shader.Find("Universal Render Pipeline/Lit")??throw new Exception("URP Lit shader missing.");
        string matPath=folder+"/Shared.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,matPath);}material.color=new Color(.65f,.8f,.95f);EditorUtility.SetDirty(material);
        string npcMatPath=folder+"/Npc.mat";var npcMaterial=AssetDatabase.LoadAssetAtPath<Material>(npcMatPath);if(npcMaterial==null){npcMaterial=new Material(shader);AssetDatabase.CreateAsset(npcMaterial,npcMatPath);}npcMaterial.color=new Color(.25f,.4f,.65f);EditorUtility.SetDirty(npcMaterial);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Bundle floor";floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(40,1,40);floor.GetComponent<Renderer>().sharedMaterial=material;
        var light=new GameObject("Bundle light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var camera=new GameObject("Bundle camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(0,5,-5);camera.transform.rotation=Quaternion.Euler(45,0,0);camera.fieldOfView=38;camera.gameObject.AddComponent<AudioListener>();
        var spawn=new GameObject("Player spawn").AddComponent<PNPlayerSpawnPoint>();spawn.Prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Eve/Prefab/Eve.prefab");spawn.Ground=floor.GetComponent<Collider>();spawn.SceneKey="world_assets:sample";spawn.transform.position=Vector3.up;
        for(int i=0;i<2;i++)
        {
            var npc=new GameObject("Bundle NPC "+i);npc.SetActive(false);npc.transform.position=new Vector3(i*2,1,0);npc.AddComponent<EveEngine.EveEntityIdentity>();var stat=npc.AddComponent<DummyStat>();var serialized=new SerializedObject(stat);serialized.FindProperty("characterId").stringValue="project_n:dummy";serialized.ApplyModifiedPropertiesWithoutUndo();npc.AddComponent<EveEngine.EveEntityLoader>();npc.AddComponent<EveEngine.EveSceneEntityKey>().Configure("world_assets:sample","npc-"+i);
            var visual=GameObject.CreatePrimitive(PrimitiveType.Capsule);visual.transform.SetParent(npc.transform,false);visual.transform.localPosition=new Vector3(0,-.5f,0);visual.transform.localScale=Vector3.one*.5f;visual.GetComponent<Collider>().enabled=false;visual.GetComponent<Renderer>().sharedMaterial=npcMaterial;
        }
        string scenePath=folder+"/Sample.unity";EditorSceneManager.SaveScene(scene,scenePath);AssetDatabase.SaveAssets();
        var builds=new[]{new AssetBundleBuild{assetBundleName="world-shared",assetNames=new[]{matPath,npcMatPath}},new AssetBundleBuild{assetBundleName="world-scene",assetNames=new[]{scenePath}}};
        var manifest=BuildPipeline.BuildAssetBundles(output,builds,BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64)??throw new Exception("Bundle build failed.");
        var bundles=new JArray();foreach(var name in manifest.GetAllAssetBundles())
        {
            var file=Path.Combine(output,name);using(var stream=File.OpenRead(file))using(var sha=System.Security.Cryptography.SHA256.Create())
            bundles.Add(new JObject{["key"]=name,["path"]=name,["bytes"]=new FileInfo(file).Length,["sha256"]=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant(),["target"]="StandaloneWindows64",["unity"]=Application.unityVersion,
                ["dependencies"]=new JArray(manifest.GetDirectDependencies(name).Select(d=>"world_assets:"+d)),["scenes"]=name=="world-scene"?new JObject{["sample"]=scenePath}:new JObject()});
        }
        File.WriteAllText(Path.Combine(output,"assets.json"),new JObject{["schema"]=1,["bundles"]=bundles}.ToString());
    }
}
#endif
