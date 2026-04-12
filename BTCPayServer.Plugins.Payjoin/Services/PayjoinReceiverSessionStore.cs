using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.Payjoin.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Payjoin;
using System;
using System.Collections.Generic;
using System.Linq;
using SystemUri = System.Uri;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinReceiverSessionStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PayjoinReceiverSessionState> _sessions = new(StringComparer.Ordinal);
    private readonly PayjoinPluginDbContextFactory _pluginDbContextFactory;
    private bool _loaded;

    public PayjoinReceiverSessionStore(PayjoinPluginDbContextFactory pluginDbContextFactory)
    {
        ArgumentNullException.ThrowIfNull(pluginDbContextFactory);
        _pluginDbContextFactory = pluginDbContextFactory;
    }

    public PayjoinReceiverSessionState CreateSession(
        string invoiceId,
        string receiverAddress,
        string storeId,
        SystemUri ohttpRelayUrl,
        DateTimeOffset monitoringExpiresAt,
        out bool created)
    {
        lock (_sync)
        {
            EnsureLoadedCore();

            if (_sessions.TryGetValue(invoiceId, out var existing))
            {
                created = false;
                return existing;
            }

            var now = DateTimeOffset.UtcNow;
            var session = new PayjoinReceiverSessionState(
                invoiceId,
                storeId,
                receiverAddress,
                ohttpRelayUrl,
                monitoringExpiresAt,
                now,
                now);

            using var context = _pluginDbContextFactory.CreateContext();
            context.ReceiverSessions.Add(CreateData(session.Snapshot()));
            context.SaveChanges();

            _sessions.Add(invoiceId, session);
            created = true;
            return session;
        }
    }

    public bool TryGetSession(string invoiceId, out PayjoinReceiverSessionState? session)
    {
        lock (_sync)
        {
            EnsureLoadedCore();
            return _sessions.TryGetValue(invoiceId, out session);
        }
    }

    public IReadOnlyCollection<PayjoinReceiverSessionState> GetSessions()
    {
        lock (_sync)
        {
            EnsureLoadedCore();
            return _sessions.Values.ToArray();
        }
    }

    public bool RemoveSession(string invoiceId)
    {
        lock (_sync)
        {
            EnsureLoadedCore();
            if (!_sessions.Remove(invoiceId))
            {
                return false;
            }

            using var context = _pluginDbContextFactory.CreateContext();
            var events = context.ReceiverSessionEvents
                .Where(x => x.InvoiceId == invoiceId)
                .ToArray();
            if (events.Length > 0)
            {
                context.ReceiverSessionEvents.RemoveRange(events);
            }

            var sessionData = context.ReceiverSessions.SingleOrDefault(x => x.InvoiceId == invoiceId);
            if (sessionData is not null)
            {
                context.ReceiverSessions.Remove(sessionData);
            }

            context.SaveChanges();
            return true;
        }
    }

    public bool RequestClose(string invoiceId, InvoiceStatus invoiceStatus)
    {
        lock (_sync)
        {
            EnsureLoadedCore();
            if (!_sessions.TryGetValue(invoiceId, out var session))
            {
                return false;
            }

            var changed = session.RequestClose(invoiceStatus, DateTimeOffset.UtcNow);
            if (!changed)
            {
                return false;
            }

            var snapshot = session.Snapshot();
            using var context = _pluginDbContextFactory.CreateContext();
            var sessionData = GetOrCreateSessionData(context, snapshot);
            ApplySnapshot(sessionData, snapshot);
            context.SaveChanges();
            return true;
        }
    }

    public bool TryPersistContributedInput(string invoiceId, OutPoint outPoint)
    {
        ArgumentNullException.ThrowIfNull(outPoint);
        lock (_sync)
        {
            EnsureLoadedCore();
            if (!_sessions.TryGetValue(invoiceId, out var session))
            {
                return false;
            }

            session.SetContributedInput(outPoint, DateTimeOffset.UtcNow);

            var snapshot = session.Snapshot();
            using var context = _pluginDbContextFactory.CreateContext();
            var sessionData = GetOrCreateSessionData(context, snapshot);
            ApplySnapshot(sessionData, snapshot);
            context.SaveChanges();
            return true;
        }
    }

    internal JsonReceiverSessionPersister CreatePersister(PayjoinReceiverSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new DatabaseBackedReceiverPersister(this, session);
    }

    private void EnsureLoadedCore()
    {
        if (_loaded)
        {
            return;
        }

        using var context = _pluginDbContextFactory.CreateContext();
        var sessionData = context.ReceiverSessions
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToArray();
        var sessionEvents = context.ReceiverSessionEvents
            .AsNoTracking()
            .OrderBy(x => x.InvoiceId)
            .ThenBy(x => x.Sequence)
            .ToArray()
            .GroupBy(x => x.InvoiceId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(e => e.Event).ToArray(), StringComparer.Ordinal);

        _sessions.Clear();
        foreach (var row in sessionData)
        {
            sessionEvents.TryGetValue(row.InvoiceId, out var events);
            _sessions[row.InvoiceId] = new PayjoinReceiverSessionState(
                row.InvoiceId,
                row.StoreId,
                row.ReceiverAddress,
                new SystemUri(row.OhttpRelayUrl, UriKind.Absolute),
                row.MonitoringExpiresAt,
                row.CreatedAt,
                row.UpdatedAt,
                row.IsCloseRequested,
                row.CloseInvoiceStatus,
                row.CloseRequestedAt,
                row.ContributedInputTransactionId,
                row.ContributedInputOutputIndex,
                events ?? []);
        }

        _loaded = true;
    }

    private void AppendEvent(PayjoinReceiverSessionState session, string @event)
    {
        lock (_sync)
        {
            EnsureLoadedCore();
            if (!_sessions.TryGetValue(session.InvoiceId, out var trackedSession) || !ReferenceEquals(trackedSession, session))
            {
                throw new InvalidOperationException($"Payjoin receiver session {session.InvoiceId} is no longer active.");
            }

            var createdAt = DateTimeOffset.UtcNow;
            var sequence = trackedSession.AddEvent(@event, createdAt);

            using var context = _pluginDbContextFactory.CreateContext();
            var snapshot = trackedSession.Snapshot();
            var sessionData = GetOrCreateSessionData(context, snapshot);
            ApplySnapshot(sessionData, snapshot);
            context.ReceiverSessionEvents.Add(new PayjoinReceiverSessionEventData
            {
                InvoiceId = snapshot.InvoiceId,
                Sequence = sequence,
                Event = @event,
                CreatedAt = createdAt
            });
            context.SaveChanges();
        }
    }

    private static PayjoinReceiverSessionData CreateData(PayjoinReceiverSessionSnapshot snapshot)
    {
        return new PayjoinReceiverSessionData
        {
            InvoiceId = snapshot.InvoiceId,
            StoreId = snapshot.StoreId,
            ReceiverAddress = snapshot.ReceiverAddress,
            OhttpRelayUrl = snapshot.OhttpRelayUrl.AbsoluteUri,
            MonitoringExpiresAt = snapshot.MonitoringExpiresAt,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt,
            IsCloseRequested = snapshot.IsCloseRequested,
            CloseInvoiceStatus = snapshot.CloseInvoiceStatus,
            CloseRequestedAt = snapshot.CloseRequestedAt,
            ContributedInputTransactionId = snapshot.ContributedInputTransactionId,
            ContributedInputOutputIndex = snapshot.ContributedInputOutputIndex
        };
    }

    private static PayjoinReceiverSessionData GetOrCreateSessionData(PayjoinPluginDbContext context, PayjoinReceiverSessionSnapshot snapshot)
    {
        var sessionData = context.ReceiverSessions.SingleOrDefault(x => x.InvoiceId == snapshot.InvoiceId);
        if (sessionData is not null)
        {
            return sessionData;
        }

        sessionData = CreateData(snapshot);
        context.ReceiverSessions.Add(sessionData);
        return sessionData;
    }

    private static void ApplySnapshot(PayjoinReceiverSessionData target, PayjoinReceiverSessionSnapshot snapshot)
    {
        target.StoreId = snapshot.StoreId;
        target.ReceiverAddress = snapshot.ReceiverAddress;
        target.OhttpRelayUrl = snapshot.OhttpRelayUrl.AbsoluteUri;
        target.MonitoringExpiresAt = snapshot.MonitoringExpiresAt;
        target.CreatedAt = snapshot.CreatedAt;
        target.UpdatedAt = snapshot.UpdatedAt;
        target.IsCloseRequested = snapshot.IsCloseRequested;
        target.CloseInvoiceStatus = snapshot.CloseInvoiceStatus;
        target.CloseRequestedAt = snapshot.CloseRequestedAt;
        target.ContributedInputTransactionId = snapshot.ContributedInputTransactionId;
        target.ContributedInputOutputIndex = snapshot.ContributedInputOutputIndex;
    }

    private sealed class DatabaseBackedReceiverPersister : JsonReceiverSessionPersister
    {
        private readonly PayjoinReceiverSessionStore _store;
        private readonly PayjoinReceiverSessionState _session;

        public DatabaseBackedReceiverPersister(PayjoinReceiverSessionStore store, PayjoinReceiverSessionState session)
        {
            _store = store;
            _session = session;
        }

        public void Save(string @event)
        {
            _store.AppendEvent(_session, @event);
        }

        public string[] Load() => _session.GetEvents();

        public void Close()
        {
        }
    }
}

public sealed class PayjoinReceiverSessionState
{
    private readonly object _sync = new();
    private readonly List<string> _events;
    private DateTimeOffset _updatedAt;
    private bool _isCloseRequested;
    private InvoiceStatus? _closeInvoiceStatus;
    private DateTimeOffset? _closeRequestedAt;
    private string? _contributedInputTransactionId;
    private int? _contributedInputOutputIndex;

    public PayjoinReceiverSessionState(
        string invoiceId,
        string storeId,
        string receiverAddress,
        SystemUri ohttpRelayUrl,
        DateTimeOffset monitoringExpiresAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isCloseRequested = false,
        InvoiceStatus? closeInvoiceStatus = null,
        DateTimeOffset? closeRequestedAt = null,
        string? contributedInputTransactionId = null,
        int? contributedInputOutputIndex = null,
        IEnumerable<string>? events = null)
    {
        InvoiceId = invoiceId;
        StoreId = storeId;
        ReceiverAddress = receiverAddress;
        OhttpRelayUrl = ohttpRelayUrl;
        MonitoringExpiresAt = monitoringExpiresAt;
        CreatedAt = createdAt;
        _updatedAt = updatedAt;
        _isCloseRequested = isCloseRequested;
        _closeInvoiceStatus = closeInvoiceStatus;
        _closeRequestedAt = closeRequestedAt;
        _contributedInputTransactionId = contributedInputTransactionId;
        _contributedInputOutputIndex = contributedInputOutputIndex;
        _events = events?.ToList() ?? [];
    }

    public string InvoiceId { get; }

    public string StoreId { get; }

    public string ReceiverAddress { get; }

    public SystemUri OhttpRelayUrl { get; }

    public DateTimeOffset MonitoringExpiresAt { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt
    {
        get
        {
            lock (_sync)
            {
                return _updatedAt;
            }
        }
    }

    public bool IsCloseRequested
    {
        get
        {
            lock (_sync)
            {
                return _isCloseRequested;
            }
        }
    }

    public InvoiceStatus? CloseInvoiceStatus
    {
        get
        {
            lock (_sync)
            {
                return _closeInvoiceStatus;
            }
        }
    }

    public bool TryGetContributedInput(out OutPoint outPoint)
    {
        lock (_sync)
        {
            outPoint = default!;

            if (string.IsNullOrWhiteSpace(_contributedInputTransactionId) || !_contributedInputOutputIndex.HasValue)
            {
                return false;
            }

            try
            {
                outPoint = new OutPoint(uint256.Parse(_contributedInputTransactionId), checked((uint)_contributedInputOutputIndex.Value));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }

    internal int AddEvent(string @event, DateTimeOffset createdAt)
    {
        lock (_sync)
        {
            _events.Add(@event);
            _updatedAt = createdAt;
            return _events.Count;
        }
    }

    internal string[] GetEvents()
    {
        lock (_sync)
        {
            return _events.ToArray();
        }
    }

    internal bool RequestClose(InvoiceStatus invoiceStatus, DateTimeOffset requestedAt)
    {
        lock (_sync)
        {
            var changed = !_isCloseRequested || _closeInvoiceStatus != invoiceStatus;
            _isCloseRequested = true;
            _closeInvoiceStatus = invoiceStatus;
            _closeRequestedAt ??= requestedAt;
            if (changed)
            {
                _updatedAt = requestedAt;
            }

            return changed;
        }
    }

    internal void SetContributedInput(OutPoint outPoint, DateTimeOffset updatedAt)
    {
        lock (_sync)
        {
            var transactionId = outPoint.Hash.ToString();
            var outputIndex = checked((int)outPoint.N);
            if (_contributedInputTransactionId == transactionId && _contributedInputOutputIndex == outputIndex)
            {
                return;
            }

            _contributedInputTransactionId = transactionId;
            _contributedInputOutputIndex = outputIndex;
            _updatedAt = updatedAt;
        }
    }

    internal PayjoinReceiverSessionSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new PayjoinReceiverSessionSnapshot(
                InvoiceId,
                StoreId,
                ReceiverAddress,
                OhttpRelayUrl,
                MonitoringExpiresAt,
                CreatedAt,
                _updatedAt,
                _isCloseRequested,
                _closeInvoiceStatus,
                _closeRequestedAt,
                _contributedInputTransactionId,
                _contributedInputOutputIndex);
        }
    }
}

internal sealed record PayjoinReceiverSessionSnapshot(
    string InvoiceId,
    string StoreId,
    string ReceiverAddress,
    SystemUri OhttpRelayUrl,
    DateTimeOffset MonitoringExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsCloseRequested,
    InvoiceStatus? CloseInvoiceStatus,
    DateTimeOffset? CloseRequestedAt,
    string? ContributedInputTransactionId,
    int? ContributedInputOutputIndex);
