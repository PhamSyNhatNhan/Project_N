#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.UIElements;

// Flow UI that stays on screen: the "Chế độ" button beside the pause toggle (Hall) that opens the menu flow (PNMenuController: mode ring ->
// setup -> [rooms] -> character); in the lobby the character button beside the pause star, the party down the left and one action at the
// bottom centre; and failure notices. No cards around any of it. All actions go through PNGameFlow, so a later in-world interactable can start
// the same flow.
internal sealed class PNModeMenu : IDisposable
{
    private readonly PNUiRuntime owner;
    private readonly PNMenuController controller;
    private readonly VisualElement team, toast;
    private readonly Button toggle, action;
    private readonly Label toastLabel;
    private string peerSignature = "", shownMessage;
    private double messageAt, lastCharacterSend;
    private bool disposed;
    private long noticeSequence=-1;
    internal const double ToastSeconds = 6;

    // Character button beside the pause star, lobby only: the icon is baked on the first lobby frame and freed with the menu.
    private readonly PNPauseMenu pause;
    private readonly Button characterToggle;
    private readonly VisualElement characterIcon;
    private Texture2D characterTexture;
    private bool characterTextureTried;

    public PNModeMenu(PNUiRuntime owner, VisualElement root, PNPauseMenu pause)
    {
        this.owner = owner; this.pause = pause; controller = new PNMenuController(owner);
        characterToggle = new Button(() => controller.OpenCharacterChange()) { name = "toggle-character", tooltip = "Nhân vật" };
        characterToggle.AddToClassList("pn-character-toggle");
        PNLocalizationUI.Watch(characterToggle,()=>characterToggle.tooltip=PNLocalization.Text("project_n:ui.character"));
        characterIcon = new VisualElement { name = "toggle-character-icon", pickingMode = PickingMode.Ignore }; characterIcon.AddToClassList("pn-character-icon");
        characterToggle.Add(characterIcon); pause.SafeArea.Add(characterToggle);
        toggle = Choice("pn-mode-toggle", "Chế độ", () => controller.OpenHall()); toggle.AddToClassList("pn-mode-toggle"); root.Add(toggle);
        PNLocalizationUI.Bind(toggle,"project_n:ui.mode");
        AddHalo(toggle, 26);
        // Lobby: the party down the left side, one action at the bottom centre in the pause buttons' look (host: Bắt đầu, others: Sẵn sàng /
        // Huỷ sẵn sàng). Leaving the room is the pause menu's "Rời phòng".
        team = new VisualElement { name = "pn-lobby-team", pickingMode = PickingMode.Ignore }; team.AddToClassList("pn-lobby-team"); root.Add(team);
        action = new Button(OnAction) { name = "pn-lobby-action" }; action.AddToClassList("pause-choice"); action.AddToClassList("pause-primary"); action.AddToClassList("pn-lobby-action"); root.Add(action);
        AddHalo(action, 21, true);
        toast = new VisualElement { name = "pn-flow-toast", pickingMode = PickingMode.Ignore }; toast.AddToClassList("pn-flow-toast");
        toastLabel = new Label(); toastLabel.AddToClassList("pn-flow-label"); toast.Add(toastLabel); root.Add(toast);
    }
    // The soft glow and thin white rim the pause buttons have; `radius` is the halo's corner radius (button radius + the 4 px it extends).
    private static void AddHalo(Button button, float radius, bool ring = false)
    {
        var halo = new PNPauseDrawing(PNPauseDrawing.Shape.ButtonHalo) { HaloRadius = radius }; halo.AddToClassList("pause-button-halo"); button.Insert(0, halo);
        // The lobby action: a clear white ring kept a few px away from the button.
        if (ring) { halo.RingAlpha = .9f; halo.RingWidth = .6f; halo.HaloRadius = radius + 2; halo.style.left = halo.style.right = halo.style.top = halo.style.bottom = -6; }
    }
    private static Button Choice(string name, string text, Action click)
    { var b = new Button(click) { name = name, text = text }; b.AddToClassList("pn-flow-button"); return b; }
    private IPNLobbyDriver Driver => PNGameBootstrap.LobbyDriver;
    // The host starts the run; anyone else toggles ready.
    private void OnAction()
    {
        var info = Driver?.Lobby; if (info == null) return;
        if (PNGameFlow.IsHost) PNGameFlow.StartRun(); else Driver.SetReady(!info.LocalReady);
    }

    // Called every frame from PNUiRuntime.RefreshUI.
    public void Tick()
    {
        if (disposed) return;
        var phase = PNGameFlow.Phase;
        bool hall = phase == PNFlowPhase.Hall && EveRegistrySession.IsReady;
        toggle.style.display = hall && !owner.MenuOpen ? DisplayStyle.Flex : DisplayStyle.None;
        TickCharacterButton(phase); TickLobby(phase); TickToast();
    }
    // The lobby's own controls hide under any open menu (pause or flow), and once the run started.
    private bool LobbyVisible(PNFlowPhase phase, out PNLobbyInfo info)
    {
        info = Driver?.Lobby;
        return phase == PNFlowPhase.Lobby && EveRegistrySession.IsReady && !owner.MenuOpen && info != null && !info.Started;
    }
    private void TickCharacterButton(PNFlowPhase phase)
    {
        bool show = LobbyVisible(phase, out _);
        characterToggle.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) return;
        if (!characterTextureTried)
        {
            characterTextureTried = true; characterTexture = PNCharacterIconArt.Build(true);
            if (characterTexture != null) characterIcon.style.backgroundImage = characterTexture;
        }
        // Beside the star: its side plus a 4 px gap, then the same 12 px margin; the icon keeps its size on the 64 px mobile button.
        float side = pause.ToggleSize;
        characterToggle.style.right = 12 + side + 4; characterToggle.style.width = side; characterToggle.style.height = side;
    }
    private void TickLobby(PNFlowPhase phase)
    {
        bool show = LobbyVisible(phase, out var info);
        team.style.display = action.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { peerSignature = ""; return; }
        bool host = PNGameFlow.IsHost;
        SendCharacter(info);
        string signature = string.Join(";", info.Peers.Select(p => p.Owner + (p.Ready ? "r" : "w") + p.Character));
        if (signature != peerSignature)
        {
            peerSignature = signature; team.Clear();
            int index = 0; foreach (var peer in info.Peers) team.Add(PeerRow(peer, index++));
        }
        string nextAction=host?"project_n:ui.start":info.LocalReady?"project_n:ui.unready":"project_n:ui.ready";
        if(actionKey!=nextAction){actionKey=nextAction;PNLocalizationUI.Bind(action,nextAction);}
        action.SetEnabled(!host || PNGameFlow.CanStartRun(out _));
    }
    // Each player's avatar rim (pastel): the host white, then pale blue, pink, green, so the same character twice is still told apart.
    private static readonly Color[] PlayerTints =
    {
        Color.white, new Color(191 / 255f, 227 / 255f, 1f), new Color(1f, 201 / 255f, 220 / 255f), new Color(201 / 255f, 240 / 255f, 192 / 255f)
    };
    private static readonly Color ReadyGold = new Color(231 / 255f, 185 / 255f, 143 / 255f);
    private string actionKey;
    // The ready mark beside a row: a dim hollow star while waiting, the same plump star filled warm gold with a soft glow when ready. The host is
    // always waiting (it starts the run, it does not ready).
    private static VisualElement ReadyMark(bool ready)
    {
        var holder = new VisualElement { pickingMode = PickingMode.Ignore }; holder.AddToClassList("pn-lobby-mark");
        var star = ready ? new PNSparkleGlyph(true, true, ReadyGold) : new PNSparkleGlyph(true, false, new Color(1f, 1f, 1f, .6f));
        star.style.position = Position.Absolute; star.style.left = 0; star.style.right = 0; star.style.top = 0; star.style.bottom = 0;
        if (ready) star.SetGlow(new Color(1f, .8f, .55f, 1f));
        holder.Add(star);
        return holder;
    }
    // One party member as on the HUD: the character's avatar (a star until it has one) with the soft glow and the thin white ring just outside,
    // then the character name over "Sẵn sàng" / "Đang chờ" (the host never readies, it only waits).
    private VisualElement PeerRow(PNLobbyPeerInfo peer, int index)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("pn-lobby-row");
        var host = new VisualElement { pickingMode = PickingMode.Ignore }; host.AddToClassList("pn-lobby-avatar-host");
        var tint = PlayerTints[index % PlayerTints.Length];
        host.Add(new PNHudGlow(true, tint));
        var ring = new VisualElement { pickingMode = PickingMode.Ignore }; ring.AddToClassList("pn-lobby-avatar-ring"); host.Add(ring);
        var avatar = new VisualElement { pickingMode = PickingMode.Ignore }; avatar.AddToClassList("pn-lobby-avatar"); host.Add(avatar);
        ring.style.borderTopColor = ring.style.borderBottomColor = ring.style.borderLeftColor = ring.style.borderRightColor = new Color(tint.r, tint.g, tint.b, .6f);
        avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = new Color(tint.r, tint.g, tint.b, .92f);
        string name = "Chưa chọn"; Sprite sprite = null; Texture2D texture = null;
        if (!string.IsNullOrEmpty(peer.Character))
        {
            try
            {
                var definition = EveRegistrySession.Current.Characters.Get(EveId.Parse(peer.Character));
                var playable = PNPlayableSettings.Parse(definition.Data);
                name = playable != null ? playable.Name : peer.Character;
                if (playable != null && !string.IsNullOrEmpty(playable.Art)) texture = Resources.Load<Texture2D>(playable.Art);
                if (texture == null && !string.IsNullOrEmpty(definition.PortraitPath)) sprite = Resources.Load<Sprite>(definition.PortraitPath);
            }
            catch (Exception e) when (e is KeyNotFoundException || e is FormatException || e is ArgumentException) { name = peer.Character; }
        }
        if (sprite != null) avatar.style.backgroundImage = new StyleBackground(sprite);
        else if (texture != null) avatar.style.backgroundImage = texture;
        else { var star = new PNPauseDrawing(PNPauseDrawing.Shape.Star); star.AddToClassList("pn-lobby-avatar-star"); avatar.Add(star); }
        var text = new VisualElement { pickingMode = PickingMode.Ignore }; text.AddToClassList("pn-lobby-text");
        var top = new Label(name) { pickingMode = PickingMode.Ignore }; top.AddToClassList("pn-lobby-name");
        if(!string.IsNullOrEmpty(peer.Character))PNLocalizationUI.Content(top,peer.Character,"character","name",name);else PNLocalizationUI.Bind(top,"project_n:ui.no_character");
        var bottom = new Label(!peer.Host && peer.Ready ? "Sẵn sàng" : "Đang chờ") { pickingMode = PickingMode.Ignore }; bottom.AddToClassList("pn-lobby-status");
        PNLocalizationUI.Bind(bottom,!peer.Host&&peer.Ready?"project_n:ui.ready":"project_n:ui.waiting");
        text.Add(top); text.Add(bottom); row.Add(host); row.Add(text);
        row.Add(ReadyMark(!peer.Host && peer.Ready));
        return row;
    }
    // The pick made in the menu is told to the host until the room shows it (the host may be slower than the first frames of the lobby).
    private void SendCharacter(PNLobbyInfo info)
    {
        string wanted = PNPlayerProfileService.Current.LastCharacter;
        if (string.IsNullOrEmpty(wanted) || info.Started) return;
        foreach (var peer in info.Peers) if (peer.Local && peer.Character == wanted) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now - lastCharacterSend < 1) return;
        lastCharacterSend = now; Driver?.SetCharacter(wanted);
    }
    private void TickToast()
    {
        string message = PNGameFlow.Message;
        if (message != shownMessage) { shownMessage = message; if(noticeSequence!=PNGameFlow.NoticeSequence){messageAt = Time.realtimeSinceStartupAsDouble;noticeSequence=PNGameFlow.NoticeSequence;}toastLabel.text = message ?? ""; }
        bool visible = !string.IsNullOrEmpty(shownMessage) && !owner.PauseOpen && Time.realtimeSinceStartupAsDouble - messageAt < ToastSeconds;
        toast.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var element in new VisualElement[] { toggle, characterToggle, team, action, toast }) element?.RemoveFromHierarchy();
        if (characterTexture != null) { UnityEngine.Object.Destroy(characterTexture); characterTexture = null; }
    }
}
#endif
