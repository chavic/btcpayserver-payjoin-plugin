using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SystemUri = System.Uri;
using BTCPayServer.Data;
using uniffi.payjoin;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinReceiverSessionStore
{
    private readonly ConcurrentDictionary<string, PayjoinReceiverSessionState> _sessions = new();

    public PayjoinReceiverSessionStore(ApplicationDbContextFactory? dbContextFactory = null)
    {
    }

    public PayjoinReceiverSessionState CreateSession(string invoiceId, string receiverAddress, string storeId, SystemUri ohttpRelayUrl)
    {
        var session = new PayjoinReceiverSessionState(invoiceId, storeId, receiverAddress, ohttpRelayUrl, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _sessions[invoiceId] = session;
        return session;
    }

    public bool TryGetSession(string invoiceId, out PayjoinReceiverSessionState session)
    {
        return _sessions.TryGetValue(invoiceId, out session!);
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
            _session.Events.Add(@event);
            _session.Touch();
        }

        public string[] Load() => _session.Events.ToArray();

        public void Close()
        {
        }
    }
}

public sealed class PayjoinReceiverSessionState
{
    public PayjoinReceiverSessionState(string invoiceId, string storeId, string receiverAddress, SystemUri ohttpRelayUrl, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        InvoiceId = invoiceId;
        StoreId = storeId;
        ReceiverAddress = receiverAddress;
        OhttpRelayUrl = ohttpRelayUrl;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Events = new List<string>();
    }

    public string InvoiceId { get; }
    public string StoreId { get; }
    public string ReceiverAddress { get; }
    public SystemUri OhttpRelayUrl { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    internal List<string> Events { get; }

    internal void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
