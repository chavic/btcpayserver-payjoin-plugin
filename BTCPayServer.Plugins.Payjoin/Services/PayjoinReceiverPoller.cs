using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using uniffi.payjoin;
using SystemUri = System.Uri;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinReceiverPoller : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogPayjoinReceiverPollingFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1, nameof(LogPayjoinReceiverPollingFailed)),
            "Payjoin receiver polling failed.");
    private static readonly Action<ILogger, string, Exception?> LogPayjoinReceiverPollingFailedForInvoice =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2, nameof(LogPayjoinReceiverPollingFailedForInvoice)),
            "Payjoin receiver polling failed for {InvoiceId}");
    private static readonly Action<ILogger, string, Exception?> LogPayjoinReceiverScriptUnavailable =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3, nameof(LogPayjoinReceiverScriptUnavailable)),
            "Payjoin receiver script unavailable for {InvoiceId}");
    private static readonly Action<ILogger, string, Exception?> LogPayjoinReceiverRelayUrlUnavailable =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, nameof(LogPayjoinReceiverRelayUrlUnavailable)),
            "Payjoin receiver relay URL unavailable for {InvoiceId}");
    private static readonly Action<ILogger, string, string, Exception?> LogPayjoinReceiverReplayFailed =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(5, nameof(LogPayjoinReceiverReplayFailed)),
            "Payjoin receiver replay failed for {InvoiceId}: {Message}");
    private readonly PayjoinDemoContext _demoContext;
    private readonly PayjoinReceiverSessionStore _sessionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PayjoinReceiverPoller> _logger;
    private readonly BTCPayNetworkProvider _networkProvider;

    public PayjoinReceiverPoller(
        PayjoinDemoContext demoContext,
        PayjoinReceiverSessionStore sessionStore,
        IHttpClientFactory httpClientFactory,
        BTCPayNetworkProvider networkProvider,
        ILogger<PayjoinReceiverPoller> logger)
    {
        _demoContext = demoContext;
        _sessionStore = sessionStore;
        _httpClientFactory = httpClientFactory;
        _networkProvider = networkProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!_demoContext.IsReady || _demoContext.OhttpRelayUrl is null)
            {
                continue;
            }

            foreach (var session in _sessionStore.GetSessions())
            {
                try
                {
                    await ProcessSessionAsync(session, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpRequestException ex)
                {
                    LogPayjoinReceiverPollingFailedForInvoice(_logger, session.InvoiceId, ex);
                }
                catch (InvalidOperationException ex)
                {
                    LogPayjoinReceiverPollingFailedForInvoice(_logger, session.InvoiceId, ex);
                }
                catch (TaskCanceledException ex)
                {
                    LogPayjoinReceiverPollingFailedForInvoice(_logger, session.InvoiceId, ex);
                }
                catch (Exception ex)
                {
                    LogPayjoinReceiverPollingFailed(_logger, ex);
                    throw;
                }
            }
        }
    }

    private async Task ProcessSessionAsync(PayjoinReceiverSessionState session, CancellationToken stoppingToken)
    {
        if (!TryGetReceiverScript(session, out var receiverScript))
        {
            LogPayjoinReceiverScriptUnavailable(_logger, session.InvoiceId, null);
            return;
        }

        if (session.OhttpRelayUrl is null)
        {
            LogPayjoinReceiverRelayUrlUnavailable(_logger, session.InvoiceId, null);
            return;
        }

        var persister = PayjoinReceiverSessionStore.CreatePersister(session);
        ReplayResult replay;
        try
        {
            replay = PayjoinMethods.ReplayReceiverEventLog(persister);
        }
        catch (ReceiverReplayException ex)
        {
            LogPayjoinReceiverReplayFailed(_logger, session.InvoiceId, ex.Message, ex);
            _sessionStore.RemoveSession(session.InvoiceId);
            return;
        }

        using var replayScope = replay;
        using var state = replayScope.State();

        switch (state)
        {
            case ReceiveSession.Initialized initialized:
                await PollInitializedAsync(initialized.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.UncheckedOriginalPayload payload:
                await ProcessUncheckedProposalAsync(payload.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.MaybeInputsOwned maybeInputsOwned:
                await ProcessMaybeInputsOwnedAsync(maybeInputsOwned.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.MaybeInputsSeen maybeInputsSeen:
                await ProcessMaybeInputsSeenAsync(maybeInputsSeen.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.OutputsUnknown outputsUnknown:
                await ProcessOutputsUnknownAsync(outputsUnknown.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.WantsOutputs wantsOutputs:
                await ProcessWantsOutputsAsync(wantsOutputs.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.WantsInputs wantsInputs:
                await ProcessWantsInputsAsync(wantsInputs.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.WantsFeeRange wantsFeeRange:
                await ProcessWantsFeeRangeAsync(wantsFeeRange.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.ProvisionalProposal provisionalProposal:
                await ProcessProvisionalProposalAsync(provisionalProposal.inner, persister, receiverScript, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
            case ReceiveSession.PayjoinProposal payjoinProposal:
                await PostPayjoinProposalAsync(payjoinProposal.inner, persister, session.OhttpRelayUrl, stoppingToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task PollInitializedAsync(
        Initialized initialized,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var requestResponse = initialized.CreatePollRequest(ohttpRelayUrl.ToString());
        var request = requestResponse.request;

        using var message = new HttpRequestMessage(HttpMethod.Post, request.url)
        {
            Content = new ByteArrayContent(request.body)
        };
        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.contentType);

        var client = _httpClientFactory.CreateClient(nameof(PayjoinReceiverPoller));
        using var response = await client.SendAsync(message, stoppingToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsByteArrayAsync(stoppingToken).ConfigureAwait(false);

        using var transition = initialized.ProcessResponse(responseBody, requestResponse.clientResponse);
        using var outcome = transition.Save(persister);

        if (outcome is InitializedTransitionOutcome.Progress progress)
        {
            await ProcessUncheckedProposalAsync(progress.inner, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessUncheckedProposalAsync(
        UncheckedOriginalPayload proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.AssumeInteractiveReceiver();
        using var maybeInputsOwned = transition.Save(persister);
        await ProcessMaybeInputsOwnedAsync(maybeInputsOwned, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessMaybeInputsOwnedAsync(
        MaybeInputsOwned proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.CheckInputsNotOwned(new ReceiverScriptOwnedCallback(receiverScript));
        using var maybeInputsSeen = transition.Save(persister);
        await ProcessMaybeInputsSeenAsync(maybeInputsSeen, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessMaybeInputsSeenAsync(
        MaybeInputsSeen proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.CheckNoInputsSeenBefore(new NoInputsSeenCallback());
        using var outputsUnknown = transition.Save(persister);
        await ProcessOutputsUnknownAsync(outputsUnknown, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessOutputsUnknownAsync(
        OutputsUnknown proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.IdentifyReceiverOutputs(new ReceiverScriptOwnedCallback(receiverScript));
        using var wantsOutputs = transition.Save(persister);
        await ProcessWantsOutputsAsync(wantsOutputs, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessWantsOutputsAsync(
        WantsOutputs proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.CommitOutputs();
        using var wantsInputs = transition.Save(persister);
        await ProcessWantsInputsAsync(wantsInputs, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessWantsInputsAsync(
        WantsInputs proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.CommitInputs();
        using var wantsFeeRange = transition.Save(persister);
        await ProcessWantsFeeRangeAsync(wantsFeeRange, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessWantsFeeRangeAsync(
        WantsFeeRange proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.ApplyFeeRange(1, 10);
        using var provisional = transition.Save(persister);
        await ProcessProvisionalProposalAsync(provisional, persister, receiverScript, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessProvisionalProposalAsync(
        ProvisionalProposal proposal,
        JsonReceiverSessionPersister persister,
        byte[] receiverScript,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var transition = proposal.FinalizeProposal(new PassthroughProcessPsbt());
        using var payjoinProposal = transition.Save(persister);
        await PostPayjoinProposalAsync(payjoinProposal, persister, ohttpRelayUrl, stoppingToken).ConfigureAwait(false);
    }

    private async Task PostPayjoinProposalAsync(
        PayjoinProposal proposal,
        JsonReceiverSessionPersister persister,
        SystemUri ohttpRelayUrl,
        CancellationToken stoppingToken)
    {
        using var requestResponse = proposal.CreatePostRequest(ohttpRelayUrl.ToString());
        var request = requestResponse.request;

        using var message = new HttpRequestMessage(HttpMethod.Post, request.url)
        {
            Content = new ByteArrayContent(request.body)
        };
        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.contentType);

        var client = _httpClientFactory.CreateClient(nameof(PayjoinReceiverPoller));
        using var response = await client.SendAsync(message, stoppingToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsByteArrayAsync(stoppingToken).ConfigureAwait(false);

        using var transition = proposal.ProcessResponse(responseBody, requestResponse.clientResponse);
        using var _ = transition.Save(persister);
    }

    private bool TryGetReceiverScript(PayjoinReceiverSessionState session, out byte[] script)
    {
        script = Array.Empty<byte>();
        var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC")?.NBitcoinNetwork;
        if (network is null)
        {
            return false;
        }

        try
        {
            var address = BitcoinAddress.Create(session.ReceiverAddress, network);
            script = address.ScriptPubKey.ToBytes();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class ReceiverScriptOwnedCallback : IsScriptOwned
    {
        private readonly byte[] _script;

        public ReceiverScriptOwnedCallback(byte[] script)
        {
            _script = script;
        }

        public bool Callback(byte[] script) => script.SequenceEqual(_script);
    }

    private sealed class NoInputsSeenCallback : IsOutputKnown
    {
        public bool Callback(PlainOutPoint _outpoint) => false;
    }

    private sealed class PassthroughProcessPsbt : ProcessPsbt
    {
        public string Callback(string psbt) => psbt;
    }
}
