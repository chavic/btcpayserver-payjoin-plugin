using BTCPayServer.Common;
using BTCPayServer.Plugins.Payjoin.Models;
using Microsoft.Extensions.Logging;
using NBitcoin;
using System;
using System.Threading;
using System.Threading.Tasks;
using uniffi.payjoin;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinBip21Service
{
    private static readonly Action<ILogger, string, Exception?> LogReceiverBuilderFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(BuildAsync)),
            "Failed to build payjoin receiver session for invoice {InvoiceId}");

    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PayjoinReceiverSessionStore _receiverSessionStore;
    private readonly PayjoinDemoContext _demoContext;
    private readonly PayjoinOhttpKeysProvider _ohttpKeysProvider;
    private readonly ILogger<PayjoinBip21Service> _logger;

    public PayjoinBip21Service(
        BTCPayNetworkProvider networkProvider,
        PayjoinReceiverSessionStore receiverSessionStore,
        PayjoinDemoContext demoContext,
        PayjoinOhttpKeysProvider ohttpKeysProvider,
        ILogger<PayjoinBip21Service> logger)
    {
        _networkProvider = networkProvider;
        _receiverSessionStore = receiverSessionStore;
        _demoContext = demoContext;
        _ohttpKeysProvider = ohttpKeysProvider;
        _logger = logger;
    }

    public async Task<PaymentUrlBuilder> BuildAsync(
        string cryptoCode,
        string destination,
        decimal due,
        PayjoinStoreSettings? storeSettings,
        bool enablePayjoin,
        string invoiceId,
        string storeId,
        CancellationToken cancellationToken)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            throw new InvalidOperationException($"Network not available for {cryptoCode}");
        }

        var bip21 = network.GenerateBIP21(destination, due);

        if (!enablePayjoin || storeSettings is null)
        {
            return bip21;
        }

        if (storeSettings.DirectoryUrl is null)
        {
            return bip21;
        }

        var directoryUrl = storeSettings.DirectoryUrl.AbsoluteUri;
        var ohttpRelayUrl = storeSettings.OhttpRelayUrl;

        if (ohttpRelayUrl is null)
        {
            return bip21;
        }

        ReadOnlyMemory<byte>? сertificate = null;
        if (storeSettings.DemoMode)
        {
            if (!_demoContext.IsReady)
            {
                return bip21;
            }

            сertificate = _demoContext.Certificate;
        }

        OhttpKeys? ohttpKeys = await _ohttpKeysProvider.GetKeysAsync(ohttpRelayUrl, directoryUrl, storeId, сertificate, cancellationToken).ConfigureAwait(false);

        if (ohttpKeys is null)
        {
            return bip21;
        }

        try
        {
            var session = _receiverSessionStore.CreateSession(invoiceId, destination, storeId, ohttpRelayUrl, out var created);
            if (created)
            {
                var persister = PayjoinReceiverSessionStore.CreatePersister(session);

                var amountSats = checked((ulong)Money.Coins(due).Satoshi);
                using var receiverBuilder = new ReceiverBuilder(destination, directoryUrl, ohttpKeys);
                using var builderWithAmount = receiverBuilder.WithAmount(amountSats);
                using var transition = builderWithAmount.Build();
                using var savedSession = transition.Save(persister);
            }
        }
        catch (UniffiException e)
        {
            _receiverSessionStore.RemoveSession(invoiceId);
            LogReceiverBuilderFailure(_logger, invoiceId, e);
            return bip21;
        }

        bip21.QueryParams.Add("pj", directoryUrl);
        return bip21;
    }
}

