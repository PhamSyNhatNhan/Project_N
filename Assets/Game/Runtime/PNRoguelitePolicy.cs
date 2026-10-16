#if UNITY_5_3_OR_NEWER
public sealed class PNRoguelitePolicy : IPNSessionPolicy
{
    private readonly PNNetworkProfile profile;
    public PNRoguelitePolicy(PNNetworkProfile profile){this.profile=profile;profile.Validate();}
    public string ModeId=>"roguelite";
    public int MaximumClients=>profile.MaximumClients;
    public double GraceSeconds=>profile.GraceSeconds;
    public void OnOwnerLeft(System.Action retire)=>retire();
}
#endif
