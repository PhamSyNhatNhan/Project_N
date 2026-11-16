#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectN.Coop.Tests
{
    public static class PNTimePolicyChecks
    {
        public static string Run()
        {
            if(EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped session required.");
            var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
            var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;int checks=0;
            void Check(bool ok){if(!ok)throw new Exception("Time policy check "+checks);checks++;}
            try
            {
                EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Client,EveContentCompatibility.Create(hub,docs,"test:t4_policy"));
                root=new GameObject("T4 policy fixture");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
                var manager=root.AddComponent<PNNetworkManager>();
                var selected=new PNNetworkProfile{GraceSeconds=2};
                manager.Policy=PNModeDefinition.Select(hub,"roguelite",selected);
                typeof(PNNetworkManager).GetField("<IsPlaying>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(manager,true);
                var service=PNTimeService.Attach(EveRegistrySession.Context,new PNMirrorTimeline(manager,EveRegistrySession.Context));
                Check(service.RemainingRecoverySeconds==2&&manager.Profile.GraceSeconds==15);
                var interrupted=typeof(PNNetworkManager).GetField("clientInterrupted",BindingFlags.Instance|BindingFlags.NonPublic);
                interrupted.SetValue(manager,Time.realtimeSinceStartupAsDouble-.5);
                double remaining=service.RemainingRecoverySeconds;
                Check(remaining>1.4&&remaining<=1.5);
                // Batch Editor may be younger than3s; a negative start is the
                // manager's "not interrupted" sentinel, not an expired attempt.
                selected.GraceSeconds=.01;manager.Policy=PNModeDefinition.Select(hub,"roguelite",selected);
                interrupted.SetValue(manager,Time.realtimeSinceStartupAsDouble-.02);
                Check(service.RemainingRecoverySeconds==0);
            }
            finally
            {
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                try{if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();}finally{EditorSceneManager.ClosePreviewScene(scene);}
            }
            return "PASS "+checks+" actual Mirror selected-policy/original-grace budget cases";
        }
    }
}
#endif
