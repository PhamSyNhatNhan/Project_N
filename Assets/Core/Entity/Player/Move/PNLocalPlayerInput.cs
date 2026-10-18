using EveEngine;

public static class PNLocalPlayerInput
{
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
