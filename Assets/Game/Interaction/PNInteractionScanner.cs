#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;

// EN: Finds the interactables the local player can use right now and keeps the entry the player chose (Docs/InteractablePlan.md I3). Local to one machine: it reads the objects of the
//     player's world from the sync service and each object's published state (open or closed), measures the flat distance to the player, and orders the ones in range by distance
//     (at most 16 are examined, at most 5 listed). The nearest entry is selected until the player chooses another one; that choice sticks, by target, while the entry stays in range.
//     An entry is Usable when the permission allows this machine and the handler's own CanUse says yes; otherwise it is listed but dimmed.
// VI: Tìm các vật tương tác người chơi cục bộ dùng được lúc này và giữ mục người chơi đã chọn: theo khoảng cách, tối đa 5 mục; mục gần nhất được chọn sẵn, lựa chọn tay giữ nguyên khi mục còn trong tầm.
public sealed class PNInteractionScanner
{
    public const int MaxExamined = 16, MaxListed = 5;
    public struct Entry
    {
        public PNInteractable Item;
        public float Distance;
        public bool Usable;
        public string LabelKey;
    }
    private readonly List<Entry> entries = new List<Entry>();
    private PNSyncTarget chosen;
    private bool manual;
    public IReadOnlyList<Entry> Entries => entries;
    public int Selected { get; private set; }

    public void Clear() { entries.Clear(); Selected = 0; manual = false; }

    public void Refresh(PNGameSession game, EveEntityLoader actor)
    {
        entries.Clear();
        var stat = actor != null ? actor.GetComponent<PNStat>() : null;
        if (game == null || game.Sync == null || actor == null || !actor.IsReady || stat == null || stat.IsDead || actor.LoadContext.Identity.World == null) { Selected = 0; manual = false; return; }
        var world = actor.LoadContext.Identity.World; var position = actor.transform.position;
        var found = new List<Entry>();
        foreach (var pair in game.Sync.ObjectsOf(world.InstanceId, world.Incarnation))
        {
            var worldObject = pair.Value as PNWorldObject;
            var item = worldObject != null ? worldObject.GetComponent<PNInteractable>() : null;
            if (item == null || !item.IsBound || !item.Available || item.Definition == null) continue;
            var anchor = item.AnchorPosition;
            float distance = new Vector2(position.x - anchor.x, position.z - anchor.z).magnitude;
            if (distance > item.Definition.Settings.Range) continue;
            found.Add(new Entry { Item = item, Distance = distance, LabelKey = item.ShownLabel });
            if (found.Count >= MaxExamined) break;
        }
        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        long owner = game.Session.Role == EveSessionRole.Client ? game.Session.LocalOwnerId.GetValueOrDefault(-1) : 0;
        for (int i = 0; i < found.Count && i < MaxListed; i++)
        {
            var entry = found[i]; var settings = entry.Item.Definition.Settings;
            bool allowed = settings.Permission == PNInteractionPermission.Everyone || game.Sync.IsAuthority;
            var context = new PNInteractionContext(game.Session, world, actor, owner, entry.Item.Definition, entry.Item.Target, entry.Item.GetComponent<PNWorldObject>(), game.Sync.IsAuthority);
            entry.Usable = allowed && entry.Item.Definition.Handler.CanUse(context).Usable;
            entries.Add(entry);
        }
        int index = manual ? entries.FindIndex(x => x.Item.Target == chosen) : -1;
        if (index < 0) { manual = false; index = 0; }
        Selected = index;
        if (entries.Count > 0) chosen = entries[Selected].Item.Target;
    }

    // The player chose another entry (a step of +1 or -1 through the list; no wrapping).
    public void Move(int step)
    {
        if (entries.Count == 0) return;
        int next = Mathf.Clamp(Selected + step, 0, entries.Count - 1);
        if (next == Selected) return;
        Selected = next; chosen = entries[next].Item.Target; manual = true;
    }

    public void Select(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        Selected = index; chosen = entries[index].Item.Target; manual = true;
    }

    public bool TryGetSelected(out Entry entry)
    {
        if (Selected >= 0 && Selected < entries.Count) { entry = entries[Selected]; return true; }
        entry = default; return false;
    }

    // Use the selected entry: false (and nothing sent) when there is none or it is dimmed. The authority runs the handler at once; a client's command goes through the transport.
    public bool UseSelected(PNGameSession game)
    {
        if (!TryGetSelected(out var entry) || !entry.Usable) return false;
        try { game.Sync.Send(entry.Item.Target, PNInteractable.Command); return true; }
        catch (Exception error) { Debug.LogException(error); return false; }
    }
}

// The input of the interaction list, owned here so it needs no change of the shared PlayerControls asset: F (or a pressed right stick) uses the selected entry, the mouse wheel (or
// the D-pad down/left) moves the selection.
internal sealed class PNInteractionControls : IDisposable
{
    public readonly InputAction Interact = new InputAction("Interact", InputActionType.Button);
    public readonly InputAction Next = new InputAction("InteractNext", InputActionType.Button);
    public readonly InputAction Previous = new InputAction("InteractPrevious", InputActionType.Button);
    public PNInteractionControls()
    {
        Interact.AddBinding("<Keyboard>/f"); Interact.AddBinding("<Gamepad>/rightStickPress");
        Next.AddBinding("<Mouse>/scroll/down"); Next.AddBinding("<Gamepad>/dpad/down");
        Previous.AddBinding("<Mouse>/scroll/up"); Previous.AddBinding("<Gamepad>/dpad/left");
        Interact.Enable(); Next.Enable(); Previous.Enable();
    }
    // The label on the selected row names the key of the device in use: the keyboard's key until a pad is touched (a stick pushed or a button pressed), "R3" (the pressed right stick) after,
    // back to the keyboard when a key is pressed.
    public bool UsingGamepad { get; private set; }
    public string KeyLabel => UsingGamepad ? "R3" : Interact.GetBindingDisplayString(0);
    public void Poll()
    {
        var pad = Gamepad.current;
        if (pad != null && pad.wasUpdatedThisFrame && (pad.leftStick.ReadValue().sqrMagnitude > .09f || pad.rightStick.ReadValue().sqrMagnitude > .09f || pad.rightStickButton.isPressed || pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed || pad.dpad.ReadValue().sqrMagnitude > 0f || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed || pad.startButton.isPressed)) UsingGamepad = true;
        else if (Keyboard.current != null && Keyboard.current.wasUpdatedThisFrame && Keyboard.current.anyKey.isPressed) UsingGamepad = false;
    }
    public void Dispose() { Interact.Dispose(); Next.Dispose(); Previous.Dispose(); }
}
#endif
