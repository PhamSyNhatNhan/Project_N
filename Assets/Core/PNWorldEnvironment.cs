#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Runtime presentation resource. Never part of durable state or network identity.
public sealed class PNWorldEnvironment
{
    private Material skybox;
    private Color ambientSky,ambientEquator,ambientGround,fogColor,subtractive;
    private AmbientMode mode;
    private float ambientIntensity,reflectionIntensity,fogDensity,fogStart,fogEnd;
    private bool fog;
    private FogMode fogMode;
    private Light sun;
    private SphericalHarmonicsL2 probe;
    private Cubemap reflection;
    private DefaultReflectionMode reflectionMode;
    public static PNWorldEnvironment Capture(Scene scene)
    {
        var previous=SceneManager.GetActiveScene();bool preview=false;
#if UNITY_EDITOR
        preview=UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene);
#endif
        bool changed=!preview&&scene!=previous;
        if(changed&&!SceneManager.SetActiveScene(scene))throw new System.InvalidOperationException("Environment source scene is unavailable.");
        try{return new PNWorldEnvironment{skybox=RenderSettings.skybox,ambientSky=RenderSettings.ambientSkyColor,ambientEquator=RenderSettings.ambientEquatorColor,ambientGround=RenderSettings.ambientGroundColor,mode=RenderSettings.ambientMode,ambientIntensity=RenderSettings.ambientIntensity,reflectionIntensity=RenderSettings.reflectionIntensity,fog=RenderSettings.fog,fogMode=RenderSettings.fogMode,fogColor=RenderSettings.fogColor,fogDensity=RenderSettings.fogDensity,fogStart=RenderSettings.fogStartDistance,fogEnd=RenderSettings.fogEndDistance,sun=RenderSettings.sun,probe=RenderSettings.ambientProbe,reflection=RenderSettings.customReflectionTexture as Cubemap,reflectionMode=RenderSettings.defaultReflectionMode,subtractive=RenderSettings.subtractiveShadowColor};}
        finally{if(changed&&previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
    public void Apply()
    {
        RenderSettings.skybox=skybox;RenderSettings.ambientMode=mode;RenderSettings.ambientSkyColor=ambientSky;RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;RenderSettings.ambientIntensity=ambientIntensity;RenderSettings.ambientProbe=probe;
        RenderSettings.reflectionIntensity=reflectionIntensity;RenderSettings.defaultReflectionMode=reflectionMode;RenderSettings.customReflectionTexture=reflection;RenderSettings.subtractiveShadowColor=subtractive;
        RenderSettings.fog=fog;RenderSettings.fogMode=fogMode;RenderSettings.fogColor=fogColor;RenderSettings.fogDensity=fogDensity;RenderSettings.fogStartDistance=fogStart;RenderSettings.fogEndDistance=fogEnd;RenderSettings.sun=sun;
    }
}
#endif
