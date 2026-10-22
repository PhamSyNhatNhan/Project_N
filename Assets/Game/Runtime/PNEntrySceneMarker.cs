#if UNITY_5_3_OR_NEWER
using UnityEngine;

// Authored on an entry scene to say which world definition that scene is and which mode a standalone start uses.
// A scene without the marker keeps the original contract: base:scene_world and the configured mode.
[DisallowMultipleComponent]
public sealed class PNEntrySceneMarker : MonoBehaviour
{
    [SerializeField] private string worldDefinition = "base:scene_world";
    // Used only when the scene is the startup scene, the role is Offline and no --pn-mode was given.
    [SerializeField] private string modeId = "";
    public string WorldDefinition => string.IsNullOrWhiteSpace(worldDefinition) ? "base:scene_world" : worldDefinition;
    public string ModeId => modeId ?? "";
    public static PNEntrySceneMarker Find(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var marker in FindObjectsByType<PNEntrySceneMarker>(FindObjectsInactive.Include))
            if (marker.gameObject.scene == scene) return marker;
        return null;
    }
}

// Scene asset paths for the roguelite flow. The scenes must stay in Build Settings.
public static class PNScenes
{
    public const string Hall = "Assets/Game/Scenes/Shared/PNHall/PNHall.unity";
    public const string Lobby = "Assets/Game/Scenes/Roguelite/PNLobby/PNLobby.unity";
    public const string Run = "Assets/Game/Scenes/Test/ErMode/ErMode.unity";
    public const string Title = "Assets/Game/Scenes/Shared/PNTitle/PNTitle.unity";
}
#endif
