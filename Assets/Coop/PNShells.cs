using Mirror;
namespace ProjectN.Coop
{
    public sealed class PNSessionShell : NetworkBehaviour
    {
        public const uint Asset = 0x504e0001;
        [SyncVar] public long Owner;
        [Command]
        public void CmdIntent(System.Guid session,long entity,long generation,long sequence,string command,string payload)
        {((PNNetworkManager)NetworkManager.singleton).RouteIntent(connectionToClient,session,entity,generation,sequence,command,payload);}
    }
    public sealed class PNActorShell : NetworkBehaviour
    {
        public const uint Asset = 0x504e0002;
        [SyncVar] public PNActorMetadata Metadata;
        private PNActorMetadata initialMetadata;
        private bool bound;
        private void ValidateMetadata()
        {
            if(Metadata.HostSession==System.Guid.Empty||Metadata.Entity<=0||Metadata.Generation<=0||Metadata.Owner<0||string.IsNullOrWhiteSpace(Metadata.Character)||Metadata.Character.Length>128||string.IsNullOrWhiteSpace(Metadata.SceneKey)||Metadata.SceneKey.Length>128||Metadata.EntityKey==null||Metadata.EntityKey.Length>128)
                throw new System.InvalidOperationException("Invalid actor shell metadata.");
            if(bound&&!Metadata.Equals(initialMetadata))throw new System.InvalidOperationException("A published shell binding is immutable; retire and spawn a new shell.");
        }
        public override void OnStartServer(){ValidateMetadata();initialMetadata=Metadata;bound=true;}
        public override void OnSerialize(NetworkWriter writer,bool initialState)
        {
            try{ValidateMetadata();base.OnSerialize(writer,initialState);}
            catch(System.Exception error){(NetworkManager.singleton as PNNetworkManager)?.ProtocolFailure(error,true);throw;}
        }
        public override void OnDeserialize(NetworkReader reader,bool initialState)
        {
            try{base.OnDeserialize(reader,initialState);ValidateMetadata();}
            catch(System.Exception error){(NetworkManager.singleton as PNNetworkManager)?.ProtocolFailure(error,false);throw;}
        }
        public override void OnStopServer()
        {if(NetworkManager.singleton is PNNetworkManager manager)manager.RemoveActor(this);}
        public override void OnStartClient()
        {
            initialMetadata=Metadata;bound=true;
            if(!isServer) ((PNNetworkManager)NetworkManager.singleton).ReceiveActor(this);
        }
        public override void OnStopClient()
        {
            if(!isServer && NetworkManager.singleton is PNNetworkManager manager)manager.RemoveActor(this);
        }
    }
}
