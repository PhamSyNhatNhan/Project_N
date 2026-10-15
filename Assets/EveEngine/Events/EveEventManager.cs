using System;
using System.Collections.Generic;

namespace EveEngine
{
    // EN: Handle used to remove one event listener when its owner ends.
    // VI: Handle dùng để gỡ một listener khi đối tượng sở hữu kết thúc.
    public sealed class EveEventSubscription : IDisposable
    {
        private Action unsubscribe;

        internal EveEventSubscription(Action unsubscribe) { this.unsubscribe = unsubscribe; }

        public void Dispose()
        {
            Action action = unsubscribe;
            unsubscribe = null;
            action?.Invoke();
        }
    }

    // EN: Dispatches typed local events with optional channels, without a registry.
    // VI: Phát event nội bộ có kiểu payload và channel tùy chọn, không dùng registry.
    public sealed class EveEventManager
    {
        // EN: Shared local bus is available before content or scene runtime initialization.
        // VI: Bus nội bộ dùng chung sẵn sàng trước khi nạp nội dung hoặc runtime của scene.
        public static EveEventManager Global { get; } = new EveEventManager();

        // EN: Stores one callback and its optional channel filter.
        // VI: Lưu một callback và bộ lọc channel tùy chọn.
        private sealed class EveListener
        {
            public EveId? Channel;
            public Action<EveEventContext, object> Invoke;
        }

        private readonly Dictionary<EveId, Type> payloadTypes = new Dictionary<EveId, Type>();
        private readonly Dictionary<EveId, List<EveListener>> listeners = new Dictionary<EveId, List<EveListener>>();

        // EN: Clear subscriptions at the start of a Unity play session without domain reload.
        // VI: Xóa listener khi bắt đầu phiên Play trong Unity dù không reload domain.
        internal void Reset()
        {
            listeners.Clear();
            payloadTypes.Clear();
        }


        // EN: A listener without a channel observes every channel of its event.
        // VI: Listener không chỉ định channel sẽ nhận event từ mọi channel.
        public EveEventSubscription Subscribe<TPayload>(EveId eventId, Action<EveEventContext, TPayload> handler, EveId? channel = null)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (channel.HasValue && !channel.Value.IsValid) throw new ArgumentException("Invalid channel ID.", nameof(channel));
            EnsurePayload<TPayload>(eventId);

            var listener = new EveListener { Channel = channel, Invoke = (context, value) => handler(context, (TPayload)value) };
            if (!listeners.TryGetValue(eventId, out List<EveListener> list))
                listeners.Add(eventId, list = new List<EveListener>());
            list.Add(listener);
            return new EveEventSubscription(() =>
            {
                list.Remove(listener);
                if (list.Count == 0 && listeners.TryGetValue(eventId, out var active) && ReferenceEquals(active, list)) listeners.Remove(eventId);
            });
        }

        public void Publish<TPayload>(EveId eventId, TPayload payload, EveId? channel = null)
        {
            if (channel.HasValue && !channel.Value.IsValid) throw new ArgumentException("Invalid channel ID.", nameof(channel));
            EnsurePayload<TPayload>(eventId);
            if (!listeners.TryGetValue(eventId, out List<EveListener> list)) return;

            var context = new EveEventContext(eventId, channel);
            foreach (EveListener listener in list.ToArray())
                if (!listener.Channel.HasValue || listener.Channel == channel)
                    listener.Invoke(context, payload);
        }

        // EN: Lifecycle notifications reach every listener even if one callback fails.
        // VI: Thông báo vòng đời đến mọi listener dù một callback bị lỗi.
        public IReadOnlyList<Exception> PublishSafely<TPayload>(EveId eventId, TPayload payload, EveId? channel = null)
        {
            if (channel.HasValue && !channel.Value.IsValid) throw new ArgumentException("Invalid channel ID.", nameof(channel));
            EnsurePayload<TPayload>(eventId);
            var errors = new List<Exception>();
            if (!listeners.TryGetValue(eventId, out var list)) return errors;
            var context = new EveEventContext(eventId, channel);
            foreach (var listener in list.ToArray())
                if (!listener.Channel.HasValue || listener.Channel == channel)
                    try { listener.Invoke(context, payload); } catch (Exception ex) { errors.Add(ex); }
            return errors;
        }

        // EN: The first use binds an event ID to one payload type for this bus's lifetime.
        // VI: Lần dùng đầu gắn ID event với một kiểu payload trong suốt vòng đời bus.
        private void EnsurePayload<TPayload>(EveId eventId)
        {
            if (!eventId.IsValid) throw new ArgumentException("Event ID is required.", nameof(eventId));
            if (!payloadTypes.TryGetValue(eventId, out Type payloadType))
            {
                payloadTypes.Add(eventId, typeof(TPayload));
                return;
            }
            if (payloadType != typeof(TPayload))
                throw new InvalidOperationException($"Event {eventId} expects {payloadType.Name}, got {typeof(TPayload).Name}.");
        }
    }

    // EN: Identifies the event and channel delivered to a listener.
    // VI: Cho listener biết ID event và channel đã nhận.
    public readonly struct EveEventContext
    {
        public EveId EventId { get; }
        public EveId? Channel { get; }

        public EveEventContext(EveId eventId, EveId? channel)
        {
            EventId = eventId;
            Channel = channel;
        }
    }
}
