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
    private readonly PayjoinPluginDbContextFactory _pluginDbContextFactory;

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
        ArgumentNullException.ThrowIfNull(ohttpRelayUrl);
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            if (TryLoadSessionCore(context, invoiceId, trackSession: false, out var existing))
            {
                created = false;
                return existing;
            }

            var now = DateTimeOffset.UtcNow;
            var sessionData = new PayjoinReceiverSessionData
            {
                InvoiceId = invoiceId,
                StoreId = storeId,
                ReceiverAddress = receiverAddress,
                OhttpRelayUrl = ohttpRelayUrl.AbsoluteUri,
                MonitoringExpiresAt = monitoringExpiresAt,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.ReceiverSessions.Add(sessionData);
            context.SaveChanges();
            created = true;
            return CreateState(sessionData);
        }
    }

    public bool TryGetSession(string invoiceId, out PayjoinReceiverSessionState? session)
    {
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            return TryLoadSessionCore(context, invoiceId, trackSession: false, out session);
        }
    }

    public IReadOnlyCollection<PayjoinReceiverSessionState> GetSessions()
    {
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            return LoadSessionsCore(context);
        }
    }

    public bool RemoveSession(string invoiceId)
    {
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            var sessionData = context.ReceiverSessions.SingleOrDefault(x => x.InvoiceId == invoiceId);
            if (sessionData is null)
            {
                return false;
            }

            var sessionEvents = context.ReceiverSessionEvents
                .Where(x => x.InvoiceId == invoiceId)
                .ToArray();
            if (sessionEvents.Length > 0)
            {
                context.ReceiverSessionEvents.RemoveRange(sessionEvents);
            }

            context.ReceiverSessions.Remove(sessionData);
            context.SaveChanges();
            return true;
        }
    }

    public bool RequestClose(string invoiceId, InvoiceStatus invoiceStatus)
    {
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            if (!TryLoadSessionCore(context, invoiceId, trackSession: true, out var session, out var sessionData))
            {
                return false;
            }

            var changed = session.RequestClose(invoiceStatus, DateTimeOffset.UtcNow);
            if (!changed)
            {
                return false;
            }

            ApplySnapshot(sessionData!, session.Snapshot());
            context.SaveChanges();
            return true;
        }
    }

    public bool TryPersistContributedInput(string invoiceId, OutPoint outPoint)
    {
        ArgumentNullException.ThrowIfNull(outPoint);
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            if (!TryLoadSessionCore(context, invoiceId, trackSession: true, out var session, out var sessionData))
            {
                return false;
            }

            session.SetContributedInput(outPoint, DateTimeOffset.UtcNow);
            ApplySnapshot(sessionData!, session.Snapshot());
            context.SaveChanges();
            return true;
        }
    }

    internal JsonReceiverSessionPersister CreatePersister(PayjoinReceiverSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new DatabaseBackedReceiverPersister(this, session.InvoiceId);
    }

    private void AppendEvent(string invoiceId, string @event)
    {
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            var sessionData = context.ReceiverSessions.SingleOrDefault(x => x.InvoiceId == invoiceId);
            if (sessionData is null)
            {
                throw new InvalidOperationException($"Payjoin receiver session {invoiceId} is no longer active.");
            }

            var createdAt = DateTimeOffset.UtcNow;
            var lastSequence = context.ReceiverSessionEvents
                .Where(x => x.InvoiceId == invoiceId)
                .Select(x => (int?)x.Sequence)
                .Max() ?? 0;
            var sequence = checked(lastSequence + 1);

            // TODO: Revisit receiver-session event retention before the log grows without bound.
            sessionData.UpdatedAt = createdAt;
            context.ReceiverSessionEvents.Add(new PayjoinReceiverSessionEventData
            {
                InvoiceId = invoiceId,
                Sequence = sequence,
                Event = @event,
                CreatedAt = createdAt
            });
            context.SaveChanges();
        }
    }

    private static PayjoinReceiverSessionState CreateState(PayjoinReceiverSessionData sessionData, string[]? events = null)
    {
        return new PayjoinReceiverSessionState(
            sessionData.InvoiceId,
            sessionData.StoreId,
            sessionData.ReceiverAddress,
            new SystemUri(sessionData.OhttpRelayUrl, UriKind.Absolute),
            sessionData.MonitoringExpiresAt,
            sessionData.CreatedAt,
            sessionData.UpdatedAt,
            sessionData.IsCloseRequested,
            sessionData.CloseInvoiceStatus,
            sessionData.CloseRequestedAt,
            sessionData.ContributedInputTransactionId,
            sessionData.ContributedInputOutputIndex,
            events ?? []);
    }

    private static IReadOnlyCollection<PayjoinReceiverSessionState> LoadSessionsCore(PayjoinPluginDbContext context)
    {
        var sessionData = context.ReceiverSessions
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToArray();
        var sessionEvents = context.ReceiverSessionEvents
            .AsNoTracking()
            .OrderBy(x => x.InvoiceId)
            .ThenBy(x => x.Sequence)
            .ToArray()
            .GroupBy(x => x.InvoiceId)
            .ToDictionary(x => x.Key, x => x.Select(e => e.Event).ToArray());

        return sessionData
            .Select(row => CreateState(row, sessionEvents.GetValueOrDefault(row.InvoiceId)))
            .ToArray();
    }

    private static bool TryLoadSessionCore(
        PayjoinPluginDbContext context,
        string invoiceId,
        bool trackSession,
        out PayjoinReceiverSessionState session)
    {
        return TryLoadSessionCore(context, invoiceId, trackSession, out session, out _);
    }

    private static bool TryLoadSessionCore(
        PayjoinPluginDbContext context,
        string invoiceId,
        bool trackSession,
        out PayjoinReceiverSessionState session,
        out PayjoinReceiverSessionData? sessionData)
    {
        var sessionQuery = trackSession
            ? context.ReceiverSessions
            : context.ReceiverSessions.AsNoTracking();
        sessionData = sessionQuery.SingleOrDefault(x => x.InvoiceId == invoiceId);
        if (sessionData is null)
        {
            session = null!;
            return false;
        }

        session = CreateState(sessionData, LoadEventsCore(context, invoiceId));
        return true;
    }

    private static string[] LoadEventsCore(PayjoinPluginDbContext context, string invoiceId)
    {
        return context.ReceiverSessionEvents
            .AsNoTracking()
            .Where(x => x.InvoiceId == invoiceId)
            .OrderBy(x => x.Sequence)
            .Select(x => x.Event)
            .ToArray();
    }

    private string[] LoadEvents(string invoiceId)
    {
        lock (_sync)
        {
            using var context = _pluginDbContextFactory.CreateContext();
            return LoadEventsCore(context, invoiceId);
        }
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
        private readonly string _invoiceId;

        public DatabaseBackedReceiverPersister(PayjoinReceiverSessionStore store, string invoiceId)
        {
            _store = store;
            _invoiceId = invoiceId;
        }

        public void Save(string @event)
        {
            _store.AppendEvent(_invoiceId, @event);
        }

        public string[] Load() => _store.LoadEvents(_invoiceId);

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
