using System.Collections.Generic;
using System.Linq;
using Mirror;

namespace ProjectN.Coop
{
    // InterestManagement.Rebuild unconditionally adds an object's owner. Required
    // initial actor state must obey subscriptions even for an authority owner.
    public sealed class PNWorldInterestManagement : InterestManagementBase
    {
        public PNNetworkManager Manager;
        public override bool OnCheckObserver(NetworkIdentity identity,NetworkConnectionToClient connection)
        {
            if(Manager==null||connection==null||!connection.isAuthenticated||!(connection.authenticationData is PNPeer))return false;
            var actor=identity.GetComponent<PNActorShell>();
            return actor==null?identity.GetComponent<PNSessionShell>()!=null:Manager.CanObserveActor(connection,actor);
        }
        public override void Rebuild(NetworkIdentity identity,bool initialize)
        {
            foreach(var connection in identity.observers.Values.ToArray())if(!connection.isReady||!OnCheckObserver(identity,connection))RemoveObserver(connection,identity);
            foreach(var connection in NetworkServer.connections.Values)if(connection.isReady&&OnCheckObserver(identity,connection)&&!identity.observers.ContainsKey(connection.connectionId))AddObserver(connection,identity);
        }
        // Actor shells have no presentation; S6 owns world presentation independently.
        public override void SetHostVisibility(NetworkIdentity identity,bool visible) { }
    }
}
