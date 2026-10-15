using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;

// EN: Shows live values for every component derived from EveStat.
// VI: Hiển thị giá trị runtime cho mọi component kế thừa EveStat.
[CustomEditor(typeof(EveStat), true)]
public class EveStatEditor : Editor
{
    // Presentation rounds to two decimals; gameplay retains double precision.
    public static string FormatValue(double value) => EveStatNumber.Format(value);
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var stat = (EveStat)target;
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Runtime stats appear here in Play Mode.", MessageType.Info);
            return;
        }
        if (!stat.IsReady)
        {
            EditorGUILayout.HelpBox("Stats have not loaded yet.", MessageType.Info);
            return;
        }

        EveStatSet set = stat.Set;
        var ids = new HashSet<EveId>(set.BaseSnapshot().Keys);
        foreach (var source in set.SourceSnapshot().Values)
            foreach (EveStatModifier modifier in source)
                ids.Add(modifier.Id);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Stats", EditorStyles.boldLabel);
        foreach (EveId id in ids.OrderBy(value => value))
        {
            set.TryGetBase(id, out EveBaseStat baseStat);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(id.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Base", baseStat == null ? "—" : FormatValue(baseStat.Value));
                if (baseStat is EveResourceStat)
                {
                    EditorGUILayout.LabelField("Final", FormatValue(set.GetMax(id)));
                    EditorGUILayout.LabelField("Current", FormatValue(set.GetCurrent(id)));
                }
                else
                    EditorGUILayout.LabelField("Final", FormatValue(set.Evaluate(id)));
            }
        }
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}
