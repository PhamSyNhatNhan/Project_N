using System;
using EveEngine;

// EN: A ground type a grid world can be painted with (Docs/SandboxPlan.md, step G1). Registered by a content module (registry `grounds`); a world keeps a palette of the ids it uses and stores the palette index per ground cell,
//     so a mod can add ground types without renumbering anything. The colour is the placeholder look until ground art exists; whether it can be walked on is a fact of the ground, not of the mesh.
// VI: Loại đất có thể sơn lên world lưới (G1). Đăng ký ở registry `grounds`; world giữ bảng các id nó dùng và lưu chỉ số bảng trên mỗi ô đất. Màu là hình tạm tới khi có art.
public sealed class PNGroundDefinition : EveIIdentified
{
    public const int MaxText = 128, MaxDescription = 256;
    public EveId Id { get; }
    public bool Walkable { get; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public string Description { get; }
    // False for a ground that is nothing: no floor is drawn over it (the colour is only what a tool shows for it).
    public bool Visible { get; }

    public PNGroundDefinition(EveId id, bool walkable, byte red, byte green, byte blue, string description = "", bool visible = true)
    {
        if (!id.IsValid || id.ToString().Length > MaxText) throw new ArgumentException("A ground type needs a valid id of at most " + MaxText + " characters.", nameof(id));
        if (description != null && description.Length > MaxDescription) throw new ArgumentException("A ground description is at most " + MaxDescription + " characters.", nameof(description));
        Id = id; Walkable = walkable; Red = red; Green = green; Blue = blue; Description = description ?? ""; Visible = visible;
    }
}

public static class PNGridIds
{
    // The world feature every grid world has (registered in PNContentModule).
    public static readonly EveId GridWorldFeature = EveId.Parse("project_n:grid_world");
    public static readonly EveId Grass = EveId.Parse("project_n:grass");
    public static readonly EveId Sand = EveId.Parse("project_n:sand");
    public static readonly EveId Stone = EveId.Parse("project_n:stone");
    // The floor of the roguelite room maps (the violet of the first scene room).
    public static readonly EveId RoomFloor = EveId.Parse("project_n:room_floor");
    // Nothing under the cell: no floor, cannot be walked on (the map tool erases ground to this).
    public static readonly EveId Void = EveId.Parse("project_n:void");
    public static readonly EveId FlatMap = EveId.Parse("project_n:flat");
    public static readonly EveId TestMazeMap = EveId.Parse("project_n:test_maze");
    public static readonly EveId SandboxTestFlat = EveId.Parse("project_n:sandbox_test_flat");
    public static readonly EveId SandboxTestMaze = EveId.Parse("project_n:sandbox_test_maze");
}
