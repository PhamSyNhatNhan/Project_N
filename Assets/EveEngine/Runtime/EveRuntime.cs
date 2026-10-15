using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EveEngine
{
    // EN: Bootstraps one content session and survives scene transitions.
    // VI: Khởi tạo một phiên nội dung và tồn tại khi chuyển scene.
    [DisallowMultipleComponent]
    public class EveRuntime : MonoBehaviour
    {
        private static EveRuntime owner;
        [SerializeField] private EveSessionRole sessionRole;
        public virtual EveSessionRole Role => sessionRole;
        public virtual string RulesVersion => "1";
        public virtual EveSessionState State => EveRegistrySession.State;
        public virtual bool IsReady => EveRegistrySession.IsReady;

        // EN: Reset static state for Editor play sessions with domain reload disabled.
        // VI: Đặt lại trạng thái tĩnh khi Play trong Editor mà không reload domain.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession()
        {
            owner = null;
            try { EveRegistrySession.End(); }
            finally { EveEventManager.Global.Reset(); }
        }

        protected virtual void Awake() => Initialize();

        protected virtual IEnumerable<EveIContentModule> CreateContentModules()
        {
            return Array.Empty<EveIContentModule>();
        }

        protected virtual IEnumerable<EveContentDocument> ReadContentDocuments()
        {
            return Array.Empty<EveContentDocument>();
        }

        protected virtual IEnumerable<EveOverrideSelection> ReadOverrideSelections() => Array.Empty<EveOverrideSelection>();
        protected virtual EveCodeModSession LoadCodeMods() => EveCodeModSession.Empty();
        protected virtual IReadOnlyDictionary<string, string> ReadGameplayAssetHashes(EveRegistryHub registry)
            => new Dictionary<string, string>();

        // EN: Build privately, publish the completed registry, then send a notification.
        // VI: Dựng registry riêng, công bố khi hoàn chỉnh rồi gửi thông báo.
        public virtual void Initialize()
        {
            if (EveRegistrySession.State == EveSessionState.Stopping)
                throw new InvalidOperationException("Finish pending session cleanup before initializing a runtime.");
            if (EveRegistrySession.State == EveSessionState.Stopped) owner = null;
            if (owner != null && owner != this)
            {
                // EN: A later scene may contain another bootstrap component; keep the existing session.
                // VI: Scene sau có thể có component bootstrap khác; giữ lại phiên đang chạy.
                Destroy(this);
                return;
            }
            if (IsReady) return;
            if (transform.parent != null)
                throw new InvalidOperationException("EveRuntime must be on a root GameObject to survive scene changes.");

            EveRegistrySession.Begin();
            owner = this;
            try
            {
                var documents = ReadContentDocuments().ToArray();
                var mods = LoadCodeMods();
                EveRegistrySession.AttachMods(mods);
                EveRegistryHub registry = new EveContentBootstrap().Build(CreateContentModules().Concat(mods.ContentModules), documents, ReadOverrideSelections());
                var compatibility = EveContentCompatibility.Create(registry, documents, RulesVersion, mods.Fingerprints, ReadGameplayAssetHashes(registry));
                DontDestroyOnLoad(gameObject);
                EveRegistrySession.Start(registry, Role, compatibility);
            }
            catch (Exception ex)
            {
                owner = null;
                EveRegistrySession.Fail(ex);
                throw;
            }
            // EN: Listener failures must not stop other entities from receiving readiness.
            // VI: Listener lỗi không được ngăn entity khác nhận thông báo sẵn sàng.
            foreach (var error in EveEventManager.Global.PublishSafely(EveRuntimeEventIds.Ready, new EveRuntimeReady()))
                Debug.LogException(error, this);
        }

        // EN: End explicitly when leaving a session; scene changes alone do not clear definitions.
        // VI: Gọi khi rời phiên chơi; riêng việc đổi scene không xóa định nghĩa.
        public virtual void EndSession()
        {
            if (owner != this) return;
            EveRegistrySession.End();
            owner = null;
        }

        protected virtual void OnDestroy()
        {
            if (owner != this) return;
            try { EndSession(); }
            catch (Exception error) { Debug.LogException(error, this); }
            finally { owner = null; }
        }
    }
}
