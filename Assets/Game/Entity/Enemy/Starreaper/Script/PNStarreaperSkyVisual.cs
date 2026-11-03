#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Presentation of the upward shot, owned by the rain area, not a second damaging
// projectile. The area supplies age; camera updates only rebuild the same age.
public sealed class PNStarreaperSkyVisual : IDisposable
{
    private readonly PNStarreaperEnergyMesh mesh;
    private Vector3 origin, direction; private float flight, age;
    private bool running, disposed;
    public PNStarreaperSkyVisual(Transform parent) { mesh = new PNStarreaperEnergyMesh(parent, "Rain sky energy"); }
    public void Begin(Vector3 at, Vector3 heading, float seconds)
    {
        Stop(); origin = at; direction = heading.sqrMagnitude > .001f ? heading.normalized : Vector3.up;
        flight = Mathf.Max(.01f, seconds); age = 0; running = true;
        PNViewCamera.Updated += OnView; OnView();
    }
    public void Advance(float seconds) { if (!running || disposed) return; age = seconds; OnView(); }
    private void OnView() { Render(PNViewCamera.Current); }
    public void Render(Camera camera)
    {
        if (!running || disposed || camera == null) return;
        float q = Mathf.Clamp01(age / flight); mesh.Begin(age);
        if (q >= 1) { mesh.End(); return; }
        var x = direction; var y = Vector3.Cross(camera.transform.forward, x).normalized;
        if (y.sqrMagnitude < .01f) y = camera.transform.up;
        var z = -camera.transform.forward * .004f;
        float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.60f, 1, q)); mesh.Opacity(fade);
        var head = origin + x * (4 * q * (.45f + .55f * q));
        float length = Mathf.Min(1.18f, .12f + q * 4);
        var tail = head - x * (length * .5f + .10f);
        mesh.Quad(false, tail + z, x, y, length, .40f, new Color(.009f,.002f,.020f,.95f), 0);
        mesh.Quad(true, tail + z*2, x, y, length, .36f, new Color(.48f,.065f,1.05f,.34f), 0);
        mesh.Quad(true, tail + z*3, x, y, length, .18f, new Color(.60f,.10f,1.2f,.14f), 6);
        mesh.Body(head, x, y, z, 1, .36f, age, 1);
        for (int i=0;i<4;i++)
        {
            float u = Mathf.Repeat(q*1.5f+i*.27f,1);
            var at = head-x*(.15f+u*length)+y*(Mathf.Sin(i*2.4f+age)*u*.09f);
            mesh.BlackStar(at,x,y,z,.025f+i*.003f,(1-u)*.65f,i*.7f);
        }
        // Fine astral fan at the upper visible edge: establishes that this shot
        // opens overhead, without a circular muzzle ring or an extra explosion.
        float fan = Mathf.Sin(Mathf.InverseLerp(.52f,1,q)*Mathf.PI);
        if (fan > .001f)
        {
            var edge = head;
            var view = camera.WorldToViewportPoint(edge);
            if(view.z>0 && view.y>.88f) edge=camera.ViewportToWorldPoint(new Vector3(Mathf.Clamp(view.x,.15f,.85f),.88f,view.z));
            for(int i=0;i<3;i++)
            {
                var a=(x+y*(i-1)*.46f).normalized;
                var b=Vector3.Cross(camera.transform.forward,a).normalized;
                var at=edge+a*(.06f+q*.14f);
                mesh.Quad(true,at+z*4,a,b,.30f+fan*.22f,.012f,new Color(.84f,.40f,1.35f,fan*.7f),1);
                mesh.Quad(true,at+z*5,a,b,.24f+fan*.13f,.004f,new Color(1.5f,.91f,.34f,fan*.65f),1);
            }
        }
        mesh.End();
    }
    public void Stop()
    {
        if(running) PNViewCamera.Updated -= OnView;
        running=false; if(!disposed) mesh.Clear();
    }
    public void Dispose() { if(disposed)return;Stop();disposed=true;mesh.Dispose(); }
}
#endif
