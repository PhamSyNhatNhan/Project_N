using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Opt-in Player review of the real menu flow on the Hall/lobby/run Flow Player (CameraFlowBuild): from the Hall it opens the menu, picks the mode,
//     the start option and the character exactly as a player would (through the screens' own entry points), and checks what the game does next.
//       -pn-menu-play-review=solo | host | join   (+ -pn-menu-play-address=<host> for join, -pn-menu-play-peers=<n> for host, -pn-menu-play-output=<dir>)
//     Not production code. Run the exe in a window (no -batchmode). Results: <dir>/result.txt (+ PNG frames).
// VI: Review Player tùy chọn cho luồng menu thật: từ Hall mở menu, chọn chế độ / cách chơi / nhân vật rồi kiểm game làm gì tiếp.
public sealed class PNMenuPlayReview : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Arg("-pn-menu-play-review=") == null) return;
        var host = new GameObject("PNMenuPlayReview"); DontDestroyOnLoad(host); host.AddComponent<PNMenuPlayReview>();
    }
    private static string Arg(string name) => Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(name, StringComparison.Ordinal))?.Substring(name.Length);
    private readonly List<string> trace = new List<string>();
    private readonly List<string> errors = new List<string>();
    private string output; private int shot;

    private void Log(string line) { trace.Add(Time.realtimeSinceStartup.ToString("0.0") + "s " + line); }
    // Every frame at the end of the frame: is the screen the blue loading cover or the scene, and what the flow says; a line is logged (and the frame
    // saved) whenever any of it changes.
    private IEnumerator FrameTimeline()
    {
        string last = ""; int frame = 0; float end = Time.realtimeSinceStartup + 14f;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture(); var px = tex.GetPixels32(); int blue = 0, n = 0; long r = 0, g = 0, b = 0;
            for (int y = 0; y < tex.height; y += 40) for (int x = 0; x < tex.width; x += 40) { var c = px[y * tex.width + x]; n++; r += c.r; g += c.g; b += c.b; if (c.b > 150 && c.r < 80 && c.g < 120) blue++; }
            string cls = blue * 10 > n * 8 ? "BLUE" : "scene";
            bool ui = PNGameBootstrap.Runtime != null && PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>() != null;
            string key = cls + " phase=" + PNGameFlow.Phase + " busy=" + PNGameFlow.Busy + " ready=" + EveRegistrySession.IsReady + " ui=" + ui + " handoff=" + PNTransitionHandoffHost.State + "/" + PNTransitionHandoffHost.Raises + "r" + PNTransitionHandoffHost.Adoptions + "a" + " screens=" + FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsInactive.Include).Count(d => d.name.Contains("Transition") || d.name.Contains("Loading Cover"));
            if (key != last)
            {
                last = key; Log("frame " + frame + ": " + key + " avg=(" + (r / n) + "," + (g / n) + "," + (b / n) + ")");
                File.WriteAllBytes(Path.Combine(output, "f" + frame.ToString("0000") + "_" + cls + ".png"), tex.EncodeToPNG());
            }
            Destroy(tex); frame++;
        }
    }
    private IEnumerator Shot(string label)
    {
        yield return new WaitForEndOfFrame();
        var screen = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, (shot++).ToString("00") + "_" + label + ".png"), screen.EncodeToPNG()); Destroy(screen);
    }
    private static IEnumerator Seconds(float s) { float until = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < until) yield return null; }

    private IEnumerator Start()
    {
        string kind = Arg("-pn-menu-play-review="); output = Arg("-pn-menu-play-output=") ?? "MenuPlay_" + kind; Directory.CreateDirectory(output);
        // -pn-menu-play-character=<id>: start the run as that character (it becomes the profile's last pick before the character screen opens).
        if (!string.IsNullOrEmpty(Arg("-pn-menu-play-character="))) PNPlayerProfileService.SetLastCharacter(Arg("-pn-menu-play-character="));
        Application.logMessageReceived += OnLog;
        Exception failure = null;
        yield return Run(kind, e => failure = e);
        Application.logMessageReceived -= OnLog;
        trace.Add("errors/exceptions logged: " + errors.Count); trace.AddRange(errors.Take(10));
        File.WriteAllText(Path.Combine(output, "result.txt"), (failure == null ? "ok" : failure.ToString()) + Environment.NewLine + string.Join(Environment.NewLine, trace));
        Application.Quit(failure == null && errors.Count == 0 ? 0 : 1);
    }
    private void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(type + ": " + message.Split('\n')[0]); }

    private IEnumerator Until(string what, Func<bool> condition, float timeout, Action<Exception> fail)
    {
        float start = Time.realtimeSinceStartup;
        while (!condition()) { if (Time.realtimeSinceStartup - start > timeout) { fail(new TimeoutException("Timed out waiting for: " + what)); yield break; } yield return null; }
        Log("reached: " + what + " after " + (Time.realtimeSinceStartup - start).ToString("0.0") + "s");
    }

    private IEnumerator Run(string kind, Action<Exception> fail)
    {
        bool failed = false; Action<Exception> Fail = e => { failed = true; fail(e); };
        PNUiRuntime ui = null;
        yield return Until("Hall ready with UI", () =>
        {
            if (!EveRegistrySession.IsReady || PNGameFlow.Phase != PNFlowPhase.Hall || PNLocalWorldContext.Current?.World == null) return false;
            ui = PNGameBootstrap.Runtime == null ? null : PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();
            return ui != null && ui.MenuFlow != null;
        }, 90, Fail);
        if (failed) yield break;
        yield return Seconds(1f);
        var flow = ui.MenuFlow; var controller = new PNMenuController(ui);
        Log("kind=" + kind + " phase=" + PNGameFlow.Phase + " timeScale=" + Time.timeScale);
        yield return Shot("hall");
        if (!controller.OpenHall()) { Fail(new InvalidOperationException("OpenHall refused.")); yield break; }
        yield return Until("mode ring shown", () => flow.Current is PNModeScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
        Log("menu open: MenuOpen=" + ui.MenuOpen + " lock=" + flow.InputLocked + " timeScale=" + Time.timeScale);
        yield return Shot("mode");
        ((PNModeScreen)flow.Current).Pick();
        yield return Until("setup shown", () => flow.Current is PNSetupScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
        yield return Shot("setup");
        int start = kind == "solo" ? 0 : kind == "host" ? 1 : 2;
        ((PNSetupScreen)flow.Current).Choose(start);
        if (kind == "join")
        {
            yield return Until("room screen shown", () => flow.Current is PNRoomScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
            yield return Shot("rooms");
            ((PNRoomScreen)flow.Current).Join(Arg("-pn-menu-play-address=") ?? "localhost");
        }
        yield return Until("character screen shown", () => flow.Current is PNCharacterScreen && !flow.IsBusy, 10, Fail); if (failed) yield break;
        var character = (PNCharacterScreen)flow.Current;
        Log("character screen: selected=" + character.Selected?.Id + " of " + "list");
        yield return Seconds(1f); yield return Shot("character");
        // -pn-menu-play-frames: log what is on screen every frame from the press until the next scene has settled (investigation of the scene change).
        if (Arg("-pn-menu-play-frames") != null) StartCoroutine(FrameTimeline());
        character.Continue();
        yield return Until("menu closed", () => !flow.IsOpen, 5, Fail); if (failed) yield break;
        Log("menu closed: lock=" + flow.InputLocked + " timeScale=" + Time.timeScale + " MenuOpen=" + ui.MenuOpen);
        string profile = Path.Combine(Application.persistentDataPath, "Profile", "profile.json");
        Log("profile file exists=" + File.Exists(profile) + (File.Exists(profile) ? " content=" + File.ReadAllText(profile).Replace("\n", " ").Replace("\r", "") : ""));

        if (kind == "solo")
        {
            yield return Until("run started (phase Run, session ready)", () => PNGameFlow.Phase == PNFlowPhase.Run && EveRegistrySession.IsReady && !PNGameFlow.Busy, 60, Fail); if (failed) yield break;
            yield return Seconds(3f); yield return Shot("run");
            Log("run: role=" + EveRegistrySession.Context.Role + " timeScale=" + Time.timeScale + " players=" + EveRegistrySession.Context.Entities.Snapshot().Count(x => PNEntityRoles.IsPlayer(x)));
            if (Arg("-pn-menu-play-pet") != null) yield return PetCase(Fail, () => failed);
            yield break;
        }
        yield return Until("lobby (room open, session ready)", () => PNGameFlow.Phase == PNFlowPhase.Lobby && EveRegistrySession.IsReady && PNGameBootstrap.LobbyDriver?.Lobby != null && !PNGameFlow.Busy, 60, Fail); if (failed) yield break;
        var driver = PNGameBootstrap.LobbyDriver;
        yield return Until("local peer shows the picked character", () => driver.Lobby != null && driver.Lobby.Peers.Any(p => p.Local && !string.IsNullOrEmpty(p.Character)), 20, Fail); if (failed) yield break;
        Log("lobby: role=" + EveRegistrySession.Context.Role + " peers=" + string.Join(", ", driver.Lobby.Peers.Select(p => p.Label + (p.Local ? "(local)" : "") + " [" + p.Character + "] ready=" + p.Ready)));
        yield return Shot("lobby");
        // -pn-menu-play-pick=<id>: change the character in the lobby and see the actor respawn as it, at the same place.
        string pick = Arg("-pn-menu-play-pick=");
        if (!string.IsNullOrEmpty(pick))
        {
            long me = kind == "host" ? 0 : EveRegistrySession.Context.LocalOwnerId.GetValueOrDefault(-1);
            Func<System.Collections.Generic.List<EveEntityLoader>> mine = () => EveRegistrySession.Context.Entities.Snapshot().Where(x => PNEntityRoles.IsPlayer(x) && x.GetComponent<EveEntityIdentity>().OwnerId == me && x.gameObject.activeInHierarchy).ToList();
            yield return Until("own player actor present", () => mine().Count == 1, 30, Fail); if (failed) yield break;
            Vector3 before = mine()[0].transform.position; string was = mine()[0].GetComponent<PNStat>().CharacterDefinitionId;
            Log("pick: before=" + was + " at " + before.ToString("F2"));
            PNPlayerProfileService.SetLastCharacter(pick);
            yield return Until("own actor is the picked character", () => { var m = mine(); return m.Count == 1 && m[0].GetComponent<PNStat>().CharacterDefinitionId == pick; }, 30, Fail); if (failed) yield break;
            yield return Seconds(1f);
            var after = mine()[0];
            Log("pick: after=" + after.GetComponent<PNStat>().CharacterDefinitionId + " at " + after.transform.position.ToString("F2") + " moved=" + Vector3.Distance(before, after.transform.position).ToString("F3") + " actors=" + mine().Count + " sprite=" + after.GetComponentInChildren<SpriteRenderer>()?.color);
            yield return Shot("lobby_changed");
        }
        if (kind == "host")
        {
            int want = int.TryParse(Arg("-pn-menu-play-peers="), out var n) ? n : 0;
            if (want > 0)
            {
                yield return Until("a peer joined with its character", () => driver.Lobby.Peers.Count(p => !p.Host && !string.IsNullOrEmpty(p.Character)) >= want, 120, Fail); if (failed) yield break;
                Log("peers: " + string.Join(", ", driver.Lobby.Peers.Select(p => p.Label + " [" + p.Character + "] ready=" + p.Ready)));
                // -pn-menu-play-pet in a coop room: the host calls the pet while both peers are in the lobby world and the client watches the replica (see PetWatch).
                if (Arg("-pn-menu-play-pet") != null) { yield return Seconds(2f); yield return PetCase(Fail, () => failed); yield return Seconds(4f); yield break; }
                yield return Until("everyone ready", () => driver.Lobby.AllReady, 60, Fail); if (failed) yield break;
                PNGameFlow.StartRun();
                yield return Until("run started", () => PNGameFlow.Phase == PNFlowPhase.Run && EveRegistrySession.IsReady && !PNGameFlow.Busy, 60, Fail); if (failed) yield break;
                float seen = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - seen < 10f && EveRegistrySession.Context.Entities.Snapshot().Count(x => PNEntityRoles.IsPlayer(x)) < 2) yield return null;
                yield return Seconds(1f); yield return Shot("run");
                Log("run (host): players=" + EveRegistrySession.Context.Entities.Snapshot().Count(x => PNEntityRoles.IsPlayer(x)));
                yield return Seconds(6f);
            }
            else yield return Seconds(3f);
            yield break;
        }
        // join: say ready, then wait for the host to start the run
        if (Arg("-pn-menu-play-pet") != null) { yield return PetWatch(); yield break; }
        driver.SetReady(true); yield return Seconds(1f); yield return Shot("ready");
        yield return Until("run started (client)", () => PNGameFlow.Phase == PNFlowPhase.Run && EveRegistrySession.IsReady && !PNGameFlow.Busy, 120, Fail); if (failed) yield break;
        yield return Seconds(8f); yield return Shot("run");
        Log("run (client): players=" + EveRegistrySession.Context.Entities.Snapshot().Count(x => PNEntityRoles.IsPlayer(x)));
    }

    // -pn-menu-play-pet (solo, started as Eve 2): the Skill slot calls the pet through the real binding. The cooldown (8 s) refuses a second press, the pet hovers near
    // the player, lives 7 s and then goes away, and the slot works again once the cooldown is over.
    private IEnumerator PetCase(Action<Exception> fail, Func<bool> hasFailed)
    {
        var loader = EveRegistrySession.Context.Entities.Snapshot().FirstOrDefault(x => PNEntityRoles.IsPlayer(x) && x.gameObject.activeInHierarchy && x.GetComponent<EveEntityIdentity>().OwnerId == 0);
        if (loader == null) { fail(new Exception("pet: no player")); yield break; }
        var stat = loader.GetComponent<PNStat>(); var skill = loader.GetComponent<PNPlayerSkill>();
        Log("pet: player " + stat.CharacterDefinitionId + ", skill " + skill.GetType().Name + ", bindings " + (skill.Bindings != null) + ", ability ready " + skill.Abilities.GetCooldown(PNSummonGameplayContent.Eve2PetReady).CanConsume());
        var press = new PNSkillInputEvent(PNSkillInputPhase.Started, PNSkillInteraction.Tap);
        Func<System.Collections.Generic.IReadOnlyList<PNSummon>> pets = () => PNSummons.Of(stat.EntityId);
        bool sent = skill.RequestSlot(PNSkillSlots.Skill, press);
        Log("pet: press dispatched=" + sent);
        yield return Until("a pet appears", () => pets().Count == 1 && pets()[0].GetComponent<EveEntityLoader>().IsReady, 10, fail); if (hasFailed()) yield break;
        var pet = pets()[0]; float born = Time.realtimeSinceStartup;
        Log("pet: summoned group=" + pet.Group + " owner=" + (pet.Summoner == stat.EntityId) + " at " + pet.transform.position.ToString("F2") + " player at " + loader.transform.position.ToString("F2") + " scale " + pet.transform.localScale.ToString("F2"));
        var sprite = pet.GetComponentInChildren<SpriteRenderer>();
        Log("pet: sprite " + (sprite != null && sprite.enabled && sprite.sprite != null) + " collider " + (pet.GetComponentInChildren<Collider>() != null) + " shots " + pet.GetComponent<PNPetShooterBehavior>().Shots);
        yield return Seconds(2f); yield return Shot("pet");
        var spot = loader.transform.position + new Vector3(.9f, .8f, -.8f);
        Log("pet: after 2 s it is " + Vector3.Distance(pet.transform.position, spot).ToString("F2") + " m from its hover spot");
        // Smoothness: walk the player for 2 s and record the pet's rendered position every frame (velocity-driven, interpolated Rigidbody).
        var mover = loader.GetComponent<PNMove>(); var petBody = pet.GetComponent<Rigidbody>();
        Log("pet: rigidbody " + (petBody != null) + " interpolation " + petBody?.interpolation + " gravity " + petBody?.useGravity);
        if (mover != null)
        {
            var speeds = new System.Collections.Generic.List<float>(); var playerSpeeds = new System.Collections.Generic.List<float>(); var relX = new System.Collections.Generic.List<float>(); var last = pet.transform.position; var playerLast = loader.transform.position; float t0 = Time.realtimeSinceStartup;
            // The review sets the move direction every frame (+X), the way held input would.
            while (Time.realtimeSinceStartup - t0 < 2f && pets().Count == 1) { mover.SetMoveDirection(new Vector2(1f, 0f)); yield return null; var now = pet.transform.position; speeds.Add((now - last).magnitude / Mathf.Max(Time.deltaTime, 1e-4f)); last = now; var pnow = loader.transform.position; playerSpeeds.Add((pnow - playerLast).magnitude / Mathf.Max(Time.deltaTime, 1e-4f)); playerLast = pnow; relX.Add(now.x - pnow.x); }
            mover.SetMoveDirection(Vector2.zero);
            if (speeds.Count > 20)
            {
                var steady = speeds.Skip(speeds.Count / 2).ToList(); float jerk = 0f, playerJerk = 0f; var ps = playerSpeeds.Skip(playerSpeeds.Count / 2).ToList();
                for (int i = 1; i < steady.Count; i++) jerk = Mathf.Max(jerk, Mathf.Abs(steady[i] - steady[i - 1]));
                for (int i = 1; i < ps.Count; i++) playerJerk = Mathf.Max(playerJerk, Mathf.Abs(ps[i] - ps[i - 1]));
                // Relative lag (pet minus player along X) in the steady half: a smooth follower keeps it near v / followRate and moves it by tiny steps from frame to frame.
                var rel = relX.Skip(relX.Count / 2).ToList(); float relStep = 0f;
                for (int i = 1; i < rel.Count; i++) relStep = Mathf.Max(relStep, Mathf.Abs(rel[i] - rel[i - 1]));
                Log("pet: lag behind the player (x) min " + rel.Min().ToString("F3") + " max " + rel.Max().ToString("F3") + " m, largest frame-to-frame change " + relStep.ToString("F4") + " m");
                Log("pet: reference, the player's own steady speed min " + ps.Min().ToString("F2") + " max " + ps.Max().ToString("F2") + ", largest frame-to-frame change " + playerJerk.ToString("F2") + " m/s");
                Log("pet: walking 2 s, " + speeds.Count + " frames, steady speed min " + steady.Min().ToString("F2") + " max " + steady.Max().ToString("F2") + " mean " + steady.Average().ToString("F2") + " m/s, largest frame-to-frame change " + jerk.ToString("F2") + " m/s; player now " + loader.transform.position.ToString("F2"));
            }
            else Log("pet: walking measurement skipped, frames " + speeds.Count);
        }
        int refusalsBefore = PNSummonCastBehavior.Refusals;
        skill.RequestSlot(PNSkillSlots.Skill, press); yield return Seconds(.3f);
        Log("pet: second press during the cooldown: pets=" + pets().Count + " refusals +" + (PNSummonCastBehavior.Refusals - refusalsBefore) + " ready=" + skill.Abilities.GetCooldown(PNSummonGameplayContent.Eve2PetReady).CanConsume());
        yield return Until("the pet goes away after its 7 s", () => pets().Count == 0, 9, fail); if (hasFailed()) yield break;
        Log("pet: gone after " + (Time.realtimeSinceStartup - born).ToString("F1") + " s; summons left " + PNSummons.LiveCount);
        yield return Until("the Skill slot is ready again", () => skill.Abilities.GetCooldown(PNSummonGameplayContent.Eve2PetReady).CanConsume(), 12, fail); if (hasFailed()) yield break;
        float ready = Time.realtimeSinceStartup;
        skill.RequestSlot(PNSkillSlots.Skill, press);
        yield return Until("a second pet appears after the cooldown", () => pets().Count == 1 && pets()[0].GetComponent<EveEntityLoader>().IsReady, 10, fail); if (hasFailed()) yield break;
        Log("pet: second pet summoned " + (Time.realtimeSinceStartup - born).ToString("F1") + " s after the first (cooldown 8 s)");
        yield return Until("the second pet goes away", () => pets().Count == 0, 9, fail);
    }

    // -pn-menu-play-pet on a client: watches the entities of the summon role for up to 40 s and says when one appears, where it is and when it goes away.
    private IEnumerator PetWatch()
    {
        Func<System.Collections.Generic.List<EveEntityLoader>> summons = () => EveRegistrySession.Context.Entities.Snapshot().Where(x => x.GetComponent<PNEntityRole>()?.Entity == "project_n:summon" && x.gameObject.activeInHierarchy).ToList();
        float start = Time.realtimeSinceStartup, appeared = -1f, gone = -1f; bool shot = false;
        while (Time.realtimeSinceStartup - start < 40f && gone < 0f)
        {
            var now = summons();
            if (now.Count > 0 && appeared < 0f)
            {
                appeared = Time.realtimeSinceStartup;
                var one = now[0]; var sprite = one.GetComponentInChildren<SpriteRenderer>();
                Log("client pet: appeared after " + (appeared - start).ToString("F1") + " s: " + one.GetComponent<PNStat>().CharacterDefinitionId + " at " + one.transform.position.ToString("F2") + " sprite " + (sprite != null && sprite.enabled && sprite.sprite != null) + " owner " + one.GetComponent<EveEntityIdentity>().OwnerId + " summon component " + (one.GetComponent<PNSummon>() != null));
            }
            else if (appeared > 0f && !shot && Time.realtimeSinceStartup - appeared > 1.5f) { shot = true; yield return Shot("client_pet"); }
            if (appeared > 0f && now.Count == 0) gone = Time.realtimeSinceStartup;
            yield return null;
        }
        Log(appeared < 0f ? "client pet: never appeared" : gone < 0f ? "client pet: never went away" : "client pet: went away after living " + (gone - appeared).ToString("F1") + " s; summon entities left " + summons().Count);
    }
}
