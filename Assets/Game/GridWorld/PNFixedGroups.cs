#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The fixed monster groups of a map (Docs/SandboxPlan.md, step G13): the entries of a `PNMapGroup` are character ids, `Count` of each, and the world makes them as entities of the role `project_n:enemy` when it is ready
//     (the factory lists them with the entities a scene authored: `SceneEntities(world)`). One entry of count 1 stands exactly at the middle of the group's cell; more are put on a ring of the group's radius around it, the same
//     places on every machine and every start. A character that is not registered is skipped with a warning (a map may name content a mod did not bring). They are not made again when they die; a world restored from a save
//     brings its own population and never asks (PNWorldRuntime.RestoredPopulation).
// VI: Nhóm quái cố định của map (G13): entry của `PNMapGroup` là id nhân vật, mỗi loại `Count` con, world tạo chúng (vai `project_n:enemy`) khi sẵn sàng. Không sinh lại khi chết.
public static class PNFixedGroups
{
    public const string SceneKey = "project_n:fixed_group", Slot = "main";

    public static IEnumerable<PNEntityRequest> Requests(EveWorldContext world)
    {
        var controller = PNGridWorldController.Find(world);
        var meta = controller?.Meta;
        if (meta == null) yield break;
        var data = controller.Data;
        foreach (var group in meta.Groups)
        {
            int index = 0, total = 0;
            foreach (var entry in group.Entries) total += entry.Count;
            foreach (var entry in group.Entries)
            {
                if (!EveId.TryParse(entry.Entity, out var character) || !world.Session.Registry.Characters.TryGet(character, out _))
                { Debug.LogWarning("Fixed group " + group.Name + ": character " + entry.Entity + " is not registered, skipped."); index += entry.Count; continue; }
                for (int i = 0; i < entry.Count; i++, index++)
                {
                    float x = (group.X + .5f) * data.CellSize, z = (group.Z + .5f) * data.CellSize;
                    if (total > 1) { double angle = Math.PI * 2 * index / total; x += (float)(Math.Cos(angle) * group.Radius); z += (float)(Math.Sin(angle) * group.Radius); }
                    yield return new PNEntityRequest
                    {
                        Entity = PNEntityGameplayContent.Enemy.ToString(), Template = PNEntityGameplayContent.Enemy.ToString(), Character = character.ToString(), World = world, WorldIncarnation = world.Incarnation,
                        ContentSlot = Slot, SceneKey = SceneKey, EntityKey = "", Position = new Vector3(x, .05f, z)
                    };
                }
            }
        }
    }
}
#endif
