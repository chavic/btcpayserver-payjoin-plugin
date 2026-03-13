using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SystemUri = System.Uri;
using BTCPayServer.Data;
using Payjoin;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinReceiverSessionStore
{
    private readonly ConcurrentDictionary<string, PayjoinReceiverSessionState> _sessions = new();

    public PayjoinReceiverSessionStore(ApplicationDbContextFactory? dbContextFactory = null)
    {
    }

    public PayjoinReceiverSessionState CreateSession(
        string invoiceId,
        string receiverAddress,
        string storeId,
        SystemUri ohttpRelayUrl,
        DateTimeOffset monitoringExpiresAt,
        out bool created)
    {
        PayjoinReceiverSessionState? createdSession = null;
        var session = _sessions.GetOrAdd(invoiceId, _ =>
        {
            createdSession = new PayjoinReceiverSessionState(
                invoiceId,
                storeId,
                receiverAddress,
                ohttpRelayUrl,
                monitoringExpiresAt,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            return createdSession;
        });
        created = ReferenceEquals(session, createdSession);
        return session;
    }

    public bool TryGetSession(string invoiceId, out PayjoinReceiverSessionState? session)
    {
        return _sessions.TryGetValue(invoiceId, out session);
    }

    public IReadOnlyCollection<PayjoinReceiverSessionState> GetSessions()
    {
        return _sessions.Values.ToArray();
    }

    public bool RemoveSession(string invoiceId)
    {
        return _sessions.TryRemove(invoiceId, out _);
    }

    internal static JsonReceiverSessionPersister CreatePersister(PayjoinReceiverSessionState session)
    {
        return new InMemoryReceiverPersister(session);
    }

    private sealed class InMemoryReceiverPersister : JsonReceiverSessionPersister
    {
        private readonly PayjoinReceiverSessionState _session;

        public InMemoryReceiverPersister(PayjoinReceiverSessionState session)
        {
            _session = session;
        }

        public void Save(string @event)
        {
            _session.AddEvent(@event);
        }

        public string[] Load() => _session.GetEvents();

        public void Close()
        {
        }
    }
}

public sealed class PayjoinReceiverSessionState
{
    private readonly ConcurrentQueue<string> _events = new();

    public PayjoinReceiverSessionState(
        string invoiceId,
        string storeId,
        string receiverAddress,
        SystemUri ohttpRelayUrl,
        DateTimeOffset monitoringExpiresAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        InvoiceId = invoiceId;
        StoreId = storeId;
        ReceiverAddress = receiverAddress;
        OhttpRelayUrl = ohttpRelayUrl;
        MonitoringExpiresAt = monitoringExpiresAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string InvoiceId { get; }
    public string StoreId { get; }
    public string ReceiverAddress { get; }
    public SystemUri OhttpRelayUrl { get; }
    public DateTimeOffset MonitoringExpiresAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal void AddEvent(string @event)
    {
        _events.Enqueue(@event);
        Touch();
    }

    internal string[] GetEvents()
    {
        return _events.ToArray();
    }

    internal void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
