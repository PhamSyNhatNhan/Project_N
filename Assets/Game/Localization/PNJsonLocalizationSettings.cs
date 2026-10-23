#if UNITY_5_3_OR_NEWER
using UnityEngine.AddressableAssets;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

// Tables and locales have already been staged by PNLocalization. The default settings
// initialize an Addressables catalogue unconditionally (LOC-823); use the public virtual
// hook rather than creating catalogue assets or modifying Unity's package.
public sealed class PNJsonLocalizationSettings : LocalizationSettings
{
    private AsyncOperationHandle<LocalizationSettings> ready;
    public override AsyncOperationHandle<LocalizationSettings> GetInitializationOperation()
    {
        if(!ready.IsValid())ready=Addressables.ResourceManager.CreateCompletedOperation<LocalizationSettings>(this,null);
        return ready;
    }
    internal void ReleaseInitialization()
    { if(ready.IsValid())Addressables.Release(ready);ready=default; }
}
#endif
