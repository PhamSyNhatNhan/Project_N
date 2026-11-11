using System;
using System.Collections.Generic;

// EN: Built-in maps for the first grid worlds (Docs/SandboxPlan.md G1/G2). They are test content made in code, not authored maps: the map tool of step G9 replaces them. `flat` is open ground; `test_maze` is the room of the
//     navigation experiments (all walls on the 0.5 m grid: two 1.0 m doorways in a divider, a 1.0 m corridor 12 m long, a field of 0.5 m pillars, a zigzag with 1.5 m openings and a 0.5 m gap), 48 x 32 m in a 96 x 64 cell world.
// VI: Map dựng sẵn bằng mã cho các world lưới đầu tiên; công cụ dựng map (G9) sẽ thay chúng.
public static class PNGridTestMaps
{
    // Rectangles in metres from the world's corner (x0, z0, x1, z1).
    private static readonly float[][] MazeWalls =
    {
        new[] { 10f, 0f, 10.5f, 3f }, new[] { 10f, 4f, 10.5f, 14f }, new[] { 10f, 15f, 10.5f, 30f },            // divider with two 1.0 m doorways
        new[] { 14f, 7f, 26f, 7.5f }, new[] { 14f, 8.5f, 26f, 9f },                                          // corridor 1.0 m wide, 12 m long
        new[] { 10.5f, 12f, 26f, 12.5f }, new[] { 10.5f, 16f, 24.5f, 16.5f }, new[] { 12f, 20f, 26f, 20.5f },   // zigzag: south wall, baffles
        new[] { 10.5f, 24f, 24.5f, 24.5f }, new[] { 10.5f, 28f, 26.5f, 28.5f }, new[] { 26f, 12f, 26.5f, 28.5f },
        new[] { 31f, 2f, 33f, 5.5f }, new[] { 31f, 6f, 33f, 10f },                                           // a 0.5 m gap
    };

    public static bool Knows(string map) => map == PNGridIds.FlatMap.ToString() || map == PNGridIds.TestMazeMap.ToString();

    // Fills the cells of a fresh world. `grounds` are palette indices to paint with (the first is the base; the second, when there is one, makes 8 m squares so a ground change can be seen).
    public static void Fill(PNGridWorldData data, string map, long seed)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (!Knows(map)) throw new ArgumentException("Unknown built-in grid map: " + map);
        int second = data.Palette.Count > 1 ? 1 : 0;
        for (int gz = 0; gz < data.GroundRows; gz++) for (int gx = 0; gx < data.GroundColumns; gx++)
            data.SetGroundCell(gx, gz, (ushort)((((gx / 4) + (gz / 4)) & 1) == 1 ? second : 0));
        if (map != PNGridIds.TestMazeMap.ToString()) return;
        foreach (var wall in MazeWalls)
        {
            int x0 = Cells(wall[0], data.CellSize), z0 = Cells(wall[1], data.CellSize), x1 = Cells(wall[2], data.CellSize) - 1, z1 = Cells(wall[3], data.CellSize) - 1;
            data.FillSolid(new PNGridRegion(x0, z0, x1, z1), true);
        }
        // pillars: 5 x 4, one cell each, every 2 m
        for (int i = 0; i < 5; i++) for (int j = 0; j < 4; j++)
        {
            int x = Cells(28f + 2 * i, data.CellSize), z = Cells(18f + 2 * j, data.CellSize);
            data.FillSolid(new PNGridRegion(x, z, x + Cells(.5f, data.CellSize) - 1, z + Cells(.5f, data.CellSize) - 1), true);
        }
    }

    private static int Cells(float metres, float cell) => (int)Math.Round(metres / cell);
}
