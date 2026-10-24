using System;

// EN: Names of the project's gameplay layers. Content refers to layers by name; PNTargetLayers compiles names to an int bitmask.
//     The table must match ProjectSettings/TagManager (a native check enforces it); smoke tests have no Unity layers.
//     Wall passes only in some cases (dash); Boundary is the invisible map volume that never passes; Void is reserved for a future mechanism.
//     Npc is added here and in TagManager together with the first NPC.
// VI: Tên các layer gameplay. Nội dung gọi layer theo tên; PNTargetLayers biên dịch tên thành bitmask int. Bảng phải khớp TagManager.
public static class PNLayers
{
    public const int Player = 6, Enemy = 7, Projectile = 8, Wall = 9, Boundary = 10, Void = 11, Ground = 13;
    private static readonly (string Name, int Index)[] known =
    {
        ("Player", Player), ("Enemy", Enemy), ("Projectile", Projectile), ("Wall", Wall), ("Boundary", Boundary), ("Void", Void), ("Ground", Ground),
    };
    public static System.Collections.Generic.IReadOnlyList<(string Name, int Index)> Known => known;
    public static int Bit(int layer) => 1 << layer;
    // The one primitive for "is this layer in the mask" (hot loops such as the projectile candidate scan keep the inline shift).
    public static bool Contains(int mask, int layer) => (uint)layer < 32u && ((mask >> layer) & 1) != 0;
    // name -> layer index; unknown names are rejected.
    public static int Index(string name, string path)
    {
        foreach (var entry in known) if (entry.Name == name) return entry.Index;
        throw new FormatException(path + ": unknown layer \"" + name + "\".");
    }
}
