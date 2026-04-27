using BTCPayServer.Plugins.Payjoin.Models;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Payjoin;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinUriSessionService
{
    private static readonly Action<ILogger, string, Exception?> LogReceiverBuilderFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(BuildAsync)),
            "Failed to build payjoin receiver session for invoice {InvoiceId}; falling back to plain BIP21.");
    private static readonly Action<ILogger, string, string, Exception?> LogExpectedPayjoinFallback =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2, nameof(LogExpectedPayjoinFallback)),
            "Payjoin not enabled for invoice {InvoiceId}: {Reason}");
    private static readonly Action<ILogger, string, string, Exception?> LogUnexpectedPayjoinFallback =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3, nameof(LogUnexpectedPayjoinFallback)),
            "Falling back to plain BIP21 for invoice {InvoiceId}: {Reason}");
    private static readonly Action<ILogger, string, Exception?> LogUninitializedSessionRebuild =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(LogUninitializedSessionRebuild)),
            "Rebuilding uninitialized payjoin receiver session for invoice {InvoiceId}.");

    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PayjoinReceiverSessionStore _receiverSessionStore;
    private readonly PayjoinOhttpKeysProvider _ohttpKeysProvider;
    private readonly PayjoinAvailabilityService _availabilityService;
    private readonly ILogger<PayjoinUriSessionService> _logger;
    private readonly Dictionary<string, SessionBuildLock> _sessionBuildLocks = new(StringComparer.Ordinal);
    private readonly object _sessionBuildLocksSync = new();

    public PayjoinUriSessionService(
        BTCPayNetworkProvider networkProvider,
        PayjoinReceiverSessionStore receiverSessionStore,
        PayjoinOhttpKeysProvider ohttpKeysProvider,
        PayjoinAvailabilityService availabilityService,
        ILogger<PayjoinUriSessionService> logger)
    {
        _networkProvider = networkProvider;
        _receiverSessionStore = receiverSessionStore;
        _ohttpKeysProvider = ohttpKeysProvider;
        _availabilityService = availabilityService;
        _logger = logger;
    }

    public async Task<string> BuildAsync(
        string cryptoCode,
        string destination,
        decimal due,
        PayjoinStoreSettings? storeSettings,
        bool enablePayjoin,
        string invoiceId,
        string storeId,
        DateTimeOffset monitoringExpiresAt,
        CancellationToken cancellationToken)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            throw new InvalidOperationException($"Network not available for {cryptoCode}");
        }

        var bip21 = network.GenerateBIP21(destination, due).ToString();

        if (!enablePayjoin)
        {
            return LogExpectedFallbackAndReturnBip21(bip21, invoiceId, "payjoin is disabled by store settings");
        }

        if (storeSettings is null)
        {
            return LogExpectedFallbackAndReturnBip21(bip21, invoiceId, "store settings are unavailable");
        }

        if (storeSettings.DirectoryUrl is null)
        {
            return LogExpectedFallbackAndReturnBip21(bip21, invoiceId, "directory URL is missing");
        }

        var directoryUrl = storeSettings.DirectoryUrl.AbsoluteUri;
        var ohttpRelayUrl = storeSettings.OhttpRelayUrl;

        if (ohttpRelayUrl is null)
        {
            return LogExpectedFallbackAndReturnBip21(bip21, invoiceId, "OHTTP relay URL is missing");
        }

        if (due <= 0m)
        {
            return LogExpectedFallbackAndReturnBip21(bip21, invoiceId, "invoice amount is not positive");
        }

        if (!await _availabilityService.HasConfirmedReceiverInputsAsync(storeId, cryptoCode, network, cancellationToken).ConfigureAwait(false))
        {
            return LogExpectedFallbackAndReturnBip21(bip21, invoiceId, "no confirmed receiver inputs are available");
        }

        OhttpKeys? ohttpKeys = await _ohttpKeysProvider.GetKeysAsync(ohttpRelayUrl, directoryUrl, storeId, cancellationToken).ConfigureAwait(false);

        if (ohttpKeys is null)
        {
            return LogUnexpectedFallbackAndReturnBip21(bip21, invoiceId, "OHTTP keys are unavailable");
        }

        try
        {
            using var sessionBuildLock = await AcquireSessionBuildLockAsync(invoiceId, cancellationToken).ConfigureAwait(false);
            var session = _receiverSessionStore.CreateSession(
                invoiceId,
                destination,
                storeId,
                ohttpRelayUrl,
                monitoringExpiresAt,
                out var created);

            if (!created && session.GetEvents().Length == 0)
            {
                LogUninitializedSessionRebuild(_logger, invoiceId, null);
                _receiverSessionStore.RemoveSession(invoiceId);
                session = _receiverSessionStore.CreateSession(
                    invoiceId,
                    destination,
                    storeId,
                    ohttpRelayUrl,
                    monitoringExpiresAt,
                    out created);
            }

            var persister = _receiverSessionStore.CreatePersister(session);
            if (created)
            {
                InitializeSession(destination, due, directoryUrl, ohttpKeys, persister);
            }

            using var replay = PayjoinMethods.ReplayReceiverEventLog(persister);
            using var history = replay.SessionHistory();
            using var pjUri = history.PjUri();
            var payjoinUri = pjUri.AsString();
            if (string.IsNullOrWhiteSpace(payjoinUri))
            {
                return LogUnexpectedFallbackAndReturnBip21(bip21, invoiceId, "payjoin URI generation returned an empty value");
            }

            return payjoinUri;
        }
        catch (ReceiverReplayException e)
        {
            _receiverSessionStore.RemoveSession(invoiceId);
            LogReceiverBuilderFailure(_logger, invoiceId, e);
            return bip21;
        }
        catch (UniffiException e)
        {
            _receiverSessionStore.RemoveSession(invoiceId);
            LogReceiverBuilderFailure(_logger, invoiceId, e);
            return bip21;
        }
    }

    private async Task<SessionBuildLockLease> AcquireSessionBuildLockAsync(string invoiceId, CancellationToken cancellationToken)
    {
        SessionBuildLock sessionBuildLock;
        lock (_sessionBuildLocksSync)
        {
            if (!_sessionBuildLocks.TryGetValue(invoiceId, out sessionBuildLock!))
            {
                sessionBuildLock = new SessionBuildLock();
                _sessionBuildLocks.Add(invoiceId, sessionBuildLock);
            }

            sessionBuildLock.ReferenceCount++;
        }

        try
        {
            await sessionBuildLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SessionBuildLockLease(this, invoiceId, sessionBuildLock);
        }
        catch
        {
            ReleaseSessionBuildLockReference(invoiceId, sessionBuildLock);
            throw;
        }
    }

    private void ReleaseSessionBuildLock(string invoiceId, SessionBuildLock sessionBuildLock)
    {
        sessionBuildLock.Semaphore.Release();
        ReleaseSessionBuildLockReference(invoiceId, sessionBuildLock);
    }

    private void ReleaseSessionBuildLockReference(string invoiceId, SessionBuildLock sessionBuildLock)
    {
        lock (_sessionBuildLocksSync)
        {
            sessionBuildLock.ReferenceCount--;
            if (sessionBuildLock.ReferenceCount == 0 &&
                _sessionBuildLocks.TryGetValue(invoiceId, out var current) &&
                ReferenceEquals(current, sessionBuildLock))
            {
                _sessionBuildLocks.Remove(invoiceId);
            }
        }
    }

    private static void InitializeSession(
        string destination,
        decimal due,
        string directoryUrl,
        OhttpKeys ohttpKeys,
        JsonReceiverSessionPersister persister)
    {
        var amountSats = checked((ulong)Money.Coins(due).Satoshi);
        using var receiverBuilder = new ReceiverBuilder(destination, directoryUrl, ohttpKeys);
        using var builderWithAmount = receiverBuilder.WithAmount(amountSats);
        using var transition = builderWithAmount.Build();
        using var savedSession = transition.Save(persister);
    }

    private string LogExpectedFallbackAndReturnBip21(string bip21, string invoiceId, string reason)
    {
        LogExpectedPayjoinFallback(_logger, invoiceId, reason, null);
        return bip21;
    }

    private string LogUnexpectedFallbackAndReturnBip21(string bip21, string invoiceId, string reason)
    {
        LogUnexpectedPayjoinFallback(_logger, invoiceId, reason, null);
        return bip21;
    }

    private sealed class SessionBuildLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class SessionBuildLockLease : IDisposable
    {
        private readonly PayjoinUriSessionService _owner;
        private readonly string _invoiceId;
        private readonly SessionBuildLock _sessionBuildLock;
        private bool _disposed;

        public SessionBuildLockLease(PayjoinUriSessionService owner, string invoiceId, SessionBuildLock sessionBuildLock)
        {
            _owner = owner;
            _invoiceId = invoiceId;
            _sessionBuildLock = sessionBuildLock;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.ReleaseSessionBuildLock(_invoiceId, _sessionBuildLock);
        }
    }
}
