#if UNITY_5_3_OR_NEWER
// The Hall is an offline-only home: choose a mode, test characters on dummies. It never opens a room itself.
public sealed class PNHallPolicy : IPNSessionPolicy
{
    private readonly PNNetworkProfile profile;
    public PNHallPolicy(PNNetworkProfile profile){this.profile=profile;profile.Validate();}
    public string ModeId=>"hall";
    public int MaximumClients=>profile.MaximumClients;
    public double GraceSeconds=>profile.GraceSeconds;
    public void OnOwnerLeft(System.Action retire)=>retire();
}
#endif
