#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

internal sealed class PNGuraSharkBite : IDisposable
{
    private static readonly Color Back = new(.012f,.31f,.52f);
    private static readonly Color Flank = new(.035f,.55f,.72f);
    private static readonly Color Belly = new(.54f,.88f,.91f);
    private static readonly Color Mouth = new(.047f,.11f,.20f);
    private static readonly Color Tooth = new(.85f,.99f,.97f);
    private readonly PNGuraVolume upper, lower;

    public PNGuraSharkBite(Transform owner)
    {
        upper = new PNGuraVolume(owner, "Gura shark upper jaw");
        lower = new PNGuraVolume(owner, "Gura shark lower jaw");
        BuildJaw(true).Apply(upper.Mesh);
        BuildJaw(false).Apply(lower.Mesh);
    }

    public void Draw(Vector3 origin, Vector3 direction, float radius, float untilHit)
    {
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.15f,-.034f,untilHit));
        float close = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.046f,0f,untilHit));
        float sink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.055f,.22f,untilHit));
        Vector3 center = origin + Vector3.up * (radius * (-.98f + 1.31f * rise - 1.29f * sink));
        center -= direction * (.22f * radius);
        Quaternion facing = Quaternion.LookRotation(direction);
        float opening = (1f - close) * 53f;
        float tilt = Mathf.Lerp(-27f,-6f,close);
        DrawJaw(upper, center, facing * Quaternion.Euler(tilt - opening * .30f,0,0), radius, origin.y);
        DrawJaw(lower, center, facing * Quaternion.Euler(tilt + opening * .70f,0,0), radius, origin.y);
    }

    private static void DrawJaw(PNGuraVolume jaw, Vector3 center, Quaternion rotation, float radius, float surface)
    {
        jaw.Transform.SetPositionAndRotation(center, rotation);
        jaw.Transform.localScale = Vector3.one * radius;
        jaw.Show(waterline: surface);
    }

    private static PNGuraFacets BuildJaw(bool top)
    {
        var mesh = new PNGuraFacets();
        float[] z = { -.78f, -.50f, -.13f, .30f, .66f, .86f, .94f };
        float[] w = { .53f, .85f, 1f, .90f, .66f, .39f, .15f };
        float[] height = top ? new[] { .36f,.63f,.67f,.49f,.27f,.16f,.06f } : new[] { .49f,.45f,.35f,.27f,.21f,.13f,.06f };
        const int facets = 8;

        Vector3 At(int row, int side)
        {
            float angle = Mathf.PI * side / facets;
            float lift = Mathf.Sin(angle) * height[row] * (top ? 1f : -1f) - (row == 0 ? .16f : 0f);
            return new Vector3(Mathf.Cos(angle) * w[row], lift, z[row]);
        }

        for (int row = 0; row < z.Length - 1; row++)
        {
            for (int side = 0; side < facets; side++)
            {
                float ridge = Mathf.Sin((side + .5f) * Mathf.PI / facets);
                var tint = top ? Color.Lerp(Belly, Back, Mathf.Lerp(.45f,.98f,ridge)) : Color.Lerp(Belly, Flank,.26f * ridge);
                mesh.Quad(At(row,side), At(row+1,side), At(row+1,side+1), At(row,side+1), tint);
            }
            mesh.Quad(At(row,0), At(row,facets), At(row+1,facets), At(row+1,0), Mouth);
        }
        int tip = z.Length - 1;
        for (int side = 0; side < facets; side++)
            mesh.Triangle(At(tip,side), At(tip,side+1), Vector3.forward * .96f, top ? Flank : Belly);

        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 2; i < z.Length - 1; i++)
            {
                Vector3 root = new Vector3(side * (w[i] - .065f),0,z[i]);
                ToothAt(mesh, root, top ? -.115f : .087f, side);
            }
            if (top)
            {
                var eye = new Vector3(side * .873f,.232f,.25f);
                var outward = new Vector3(side * .82f,.18f,.38f).normalized;
                var horizontal = Vector3.Cross(Vector3.up,outward).normalized;
                Disc(mesh, eye, horizontal*.115f, Vector3.up*.084f, Mouth);
                Disc(mesh, eye + outward*.015f, horizontal*.067f, Vector3.up*.054f, Tooth);
                Disc(mesh, eye + outward*.027f + Vector3.forward*.017f, horizontal*.034f, Vector3.up*.043f, Mouth);
            }
        }

        for (int i = -2; i <= 2; i++)
            ToothAt(mesh, new Vector3(i * .144f,0,.89f - Mathf.Abs(i)*.022f), top ? -.128f : .082f, 1);
        return mesh;
    }

    private static void ToothAt(PNGuraFacets mesh, Vector3 center, float height, int side)
    {
        Vector3 a = center + new Vector3(-.045f,0,-.039f);
        Vector3 b = center + new Vector3(.045f,0,-.039f);
        Vector3 c = center + new Vector3(0,0,.054f);
        Vector3 tip = center + new Vector3(-side*.024f,height,.019f);
        mesh.Triangle(a,b,tip,Tooth);
        mesh.Triangle(b,c,tip,Belly);
        mesh.Triangle(c,a,tip,Tooth);
    }

    private static void Disc(PNGuraFacets mesh, Vector3 center, Vector3 x, Vector3 y, Color color)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            float b = (i+1) * Mathf.PI / 4f;
            mesh.Triangle(center, center + x * Mathf.Cos(a) + y * Mathf.Sin(a), center + x * Mathf.Cos(b) + y * Mathf.Sin(b), color);
        }
    }

    public void Hide() { upper.Hide(); lower.Hide(); }
    public void Dispose() { upper.Dispose(); lower.Dispose(); }
}
#endif
