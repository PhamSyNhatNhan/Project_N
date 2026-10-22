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
        private bool hasStartedPlay;
        private bool loadingContent;
        public EveContentContext Content { get; private set; }
        public bool IsContentReady => Content != null && Content.IsReady;
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
            var previous = owner;
            owner = null;
            try
            {
                if (previous != null && previous.Content != null) previous.Content.RequestUnload();
                EveRegistrySession.End();
                if (previous != null && previous.Content != null) EveRegistrySession.Unload(previous.Content);
            }
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
        protected virtual void BindSession(EveSessionContext session) { }
        protected virtual IReadOnlyDictionary<string, string> ReadGameplayAssetHashes(EveRegistryHub registry)
            => new Dictionary<string, string>();

        // Explicit convenience for standalone/offline callers; subsequent plays reuse loaded content.
        public virtual void Initialize()
        {
            EnsureContentLoaded();
            if (owner == this) BeginSession(Role);
        }

        public virtual void EnsureContentLoaded()
        {
            // One load, whether it runs all at once (here) or step by step (ContentLoadSteps).
            var steps = ContentLoadSteps();
            while (steps.MoveNext()) { }
        }

        // EN: The same content load as EnsureContentLoaded, one named step at a time (documents, code mods, registry, compatibility, publish),
        //     so a caller can run it across frames under a loading screen and report progress. Guards run at the first MoveNext; the load is
        //     "already running" until the last step or until the enumerator is disposed. Failure rolls back exactly as the one-shot load does
        //     and is thrown from the MoveNext that hit it.
        // VI: Cùng việc nạp nội dung như EnsureContentLoaded nhưng từng bước có tên để chạy qua nhiều khung dưới màn Loading và báo tiến độ.
        public IEnumerator<EveBootStep> ContentLoadSteps()
        {
            if (loadingContent) throw new InvalidOperationException("Content initialization is already running.");
            if (EveRegistrySession.State == EveSessionState.Stopping)
                throw new InvalidOperationException("Finish pending session cleanup before initializing a runtime.");
            // An explicitly created replacement can take over an idle runtime, but must release its content first.
            if (owner != null && owner != this && owner.hasStartedPlay &&
                (EveRegistrySession.State == EveSessionState.Stopped || EveRegistrySession.State == EveSessionState.Failed)) owner.UnloadContent();
            if (owner != null && owner != this)
            {
                // EN: A later scene may contain another bootstrap component; keep the existing session.
                // VI: Scene sau có thể có component bootstrap khác; giữ lại phiên đang chạy.
                Destroy(this);
                yield break;
            }
            if (IsContentReady) { owner = this; yield break; }
            if (transform.parent != null)
                throw new InvalidOperationException("EveRuntime must be on a root GameObject to survive scene changes.");

            owner = this;
            loadingContent = true;
            Exception failure = null;
            try
            {
                var inner = LoadContentInner();
                while (true)
                {
                    bool more;
                    try { more = inner.MoveNext(); }
                    catch (Exception ex) { failure = ex; break; }
                    if (!more) break;
                    yield return inner.Current;
                }
            }
            finally { loadingContent = false; }
            if (failure != null)
            {
                owner = null;
                EveRegistrySession.FailContent(Content, failure);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            foreach (var error in EveEventManager.Global.PublishSafely(EveRuntimeEventIds.ContentReady, new EveContentReady(Content)))
                Debug.LogException(error, this);
        }

        private IEnumerator<EveBootStep> LoadContentInner()
        {
            Content?.Dispose();
            var documents = ReadContentDocuments().ToArray();
            yield return new EveBootStep("content documents", .15f);
            var mods = LoadCodeMods();
            yield return new EveBootStep("code mods", .35f);
            Content = new EveContentContext(mods);
            EveRegistryHub registry = new EveContentBootstrap().Build(CreateContentModules().Concat(mods.ContentModules), documents, ReadOverrideSelections());
            yield return new EveBootStep("content registry", .8f);
            var compatibility = EveContentCompatibility.Create(registry, documents, RulesVersion, mods.Fingerprints, ReadGameplayAssetHashes(registry));
            yield return new EveBootStep("compatibility", .95f);
            Content.Publish(registry, compatibility);
#if UNITY_5_3_OR_NEWER
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
#else
            DontDestroyOnLoad(gameObject);
#endif
            yield return new EveBootStep("published", 1f);
        }

        public virtual void BeginSession(EveSessionRole role)
        {
            if (!Enum.IsDefined(typeof(EveSessionRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            EnsureContentLoaded();
            if (owner != this) return;
            if (IsReady)
            {
                if (EveRegistrySession.Context.Role != role) throw new InvalidOperationException("End the current session before changing Role.");
                return;
            }
            EveRegistrySession.Begin(Content);
            hasStartedPlay = true;
            try
            {
                EveRegistrySession.Start(Content.Registry, role, Content.Compatibility);
                BindSession(EveRegistrySession.Context);
            }
            catch (Exception ex) { EveRegistrySession.Fail(ex); throw; }
            // EN: Listener failures must not stop other entities from receiving readiness.
            // VI: Listener lỗi không được ngăn entity khác nhận thông báo sẵn sàng.
            foreach (var error in EveEventManager.Global.PublishSafely(EveRuntimeEventIds.Ready, new EveRuntimeReady()))
                Debug.LogException(error, this);
        }

        // End only the play; registered content and mod instances remain available.
        public virtual void EndSession()
        {
            if (owner != this) return;
            EveRegistrySession.End();
        }

        public virtual void UnloadContent()
        {
            if (Content == null) return;
            Content.RequestUnload();
            if (owner == this && (EveRegistrySession.State == EveSessionState.Ready || EveRegistrySession.State == EveSessionState.Stopping))
                EndSession();
            if (EveRegistrySession.State == EveSessionState.Stopping) return;
            EveRegistrySession.Unload(Content);
            Content = null;
            if (owner == this) owner = null;
        }

        protected virtual void OnDestroy()
        {
            if (Content == null) return;
            try { UnloadContent(); }
            catch (Exception error) { Debug.LogException(error, this); }
            finally { if (owner == this) owner = null; }
        }
    }
}
