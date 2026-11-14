using EveEngine;

public static class PNLocalPlayerInput
{
    // EN: May the AUTHORITY run input that another machine's owner sent for this entity? It is the entity's own state only: ready, not held, the network input gate open, the session active. Who the local player is does not matter.
    // VI: Authority có được chạy input mà chủ ở máy khác gửi cho entity này? Chỉ xét trạng thái của entity: sẵn sàng, không bị giữ, cổng input mạng mở, phiên đang chạy.
    public static bool AllowsRemote(EveEntityLoader loader)
    {
        if(loader==null||loader.GetComponent<PNNetworkInputGate>()?.Blocked==true)return false;
        if(!loader.IsReady||loader.LoadContext==null||!loader.LoadContext.CanExecute)return false;
        return EveRegistrySession.IsReady&&EveRegistrySession.Context.IsActive;
    }
    public static bool Allows(EveEntityLoader loader)
    {
        if(loader!=null && loader.GetComponent<PNNetworkInputGate>()?.Blocked==true)return false;
        if(loader!=null && (!loader.IsReady || loader.LoadContext==null || !loader.LoadContext.CanExecute))return false;
        var current=EveRegistrySession.IsReady?EveRegistrySession.Context:null;
        if(current==null)return loader==null; // Preserve controllers outside the registered entity pipeline.
        if(!current.IsActive)return false;
        if(current.Role==EveSessionRole.Offline)return true;
        if(loader==null || !loader.IsReady || loader.LoadContext==null || !loader.LoadContext.CanExecute)return false;
        var session=loader.LoadContext.Session; var identity=loader.LoadContext.Identity;
        if(session.Role==EveSessionRole.Offline)return true;
        if(session.Role==EveSessionRole.Host)return identity.OwnerId==0;
        return session.LocalOwnerId.HasValue && identity.OwnerId==session.LocalOwnerId.Value;
    }
}
