#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Native checks for the Eve 2 prefab: a copy of the Eve prefab with its own skill / stat classes and a slightly red sprite.
public static class PNEve2PrefabEditorChecks
{
    public static string Run()
    {
        int checks = 0; void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var eve = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Eve/Prefab/Eve.prefab");
        var eve2 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Eve2/Prefab/Eve2.prefab");
        Check(eve != null && eve2 != null, "both prefabs load");
        var eveParts = eve.GetComponentsInChildren<Component>(true); var eve2Parts = eve2.GetComponentsInChildren<Component>(true);
        Check(eve2Parts.All(c => c != null), "no missing script on the Eve 2 prefab");
        Check(eve2Parts.Length == eveParts.Length, "Eve 2 has the same number of components as Eve");
        Check(eve2.GetComponent<PNEve2Skill>() != null && eve2.GetComponent<PNEve2Stat>() != null, "Eve 2 carries its own skill and stat classes");
        Check(eve2.GetComponent<EveCharacterSkill>() == null && eve2.GetComponent<EveCharacterStat>() == null, "Eve 2 does not inherit Eve's skill or stat classes");
        Check(eve2.GetComponent<PNPlayerSkill>() is PNEve2Skill && eve2.GetComponent<PNPlayerStat>() is PNEve2Stat && typeof(PNEve2Skill).BaseType == typeof(PNPlayerSkill) && typeof(PNEve2Stat).BaseType == typeof(PNPlayerStat), "they derive directly from the ordinary player skill and stat");
        Check(new SerializedObject(eve2.GetComponent<PNEve2Stat>()).FindProperty("characterId").stringValue == "project_n:eve_2" && new SerializedObject(eve2.GetComponent<PNEve2Skill>()).FindProperty("characterId").stringValue == "project_n:eve_2", "both components point at the project_n:eve_2 character");
        Check(eve.GetComponent<PNEve2Skill>() == null && eve.GetComponent<PNEve2Stat>() == null, "the Eve prefab is untouched");
        Check(eve2.GetComponents<PNPlayerSkill>().Length == 1 && eve2.GetComponents<PNPlayerStat>().Length == 1, "one skill and one stat component, not two");
        var names = eve2Parts.Select(c => c.GetType().Name.Replace("PNEve2", "EveCharacter").Replace("EveCharacterSkill", "EveCharacterSkill")).OrderBy(x => x).ToArray();
        var eveNames = eveParts.Select(c => c.GetType().Name).OrderBy(x => x).ToArray();
        Check(names.SequenceEqual(eveNames), "the component types match Eve's apart from the two new classes");
        var sprite = eve2.GetComponentInChildren<SpriteRenderer>(true); var eveSprite = eve.GetComponentInChildren<SpriteRenderer>(true);
        Check(sprite.color.r == 1f && sprite.color.g < 1f && sprite.color.g > .5f && Mathf.Approximately(sprite.color.g, sprite.color.b) && sprite.sprite == eveSprite.sprite, "the Eve 2 sprite is slightly red and uses Eve's sprite");
        Check(eveSprite.color == Color.white, "the Eve sprite colour is unchanged");
        Check(eve2.name == "Eve2" && eve2.layer == eve.layer && eve2.tag == eve.tag, "name Eve2, same layer and tag as Eve");
        var shadows = eve2.GetComponentsInChildren<SpriteRenderer>(true).Skip(1).Select(s => s.color).ToArray(); var eveShadows = eve.GetComponentsInChildren<SpriteRenderer>(true).Skip(1).Select(s => s.color).ToArray();
        Check(shadows.SequenceEqual(eveShadows), "other sprites (the ground shadow) keep their colours");
        return "PASS " + checks + " Eve 2 prefab checks";
    }
}
#endif
