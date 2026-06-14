using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using NBitcoin;
using NBXplorer;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Services;

internal interface IPayjoinWalletOwnershipService
{
    /// <summary>
    /// Builds a resolver that can answer whether a script is owned by the store's BTC derivation scheme.
    /// Resolving the derivation and explorer client once lets the synchronous rust-payjoin ownership
    /// callbacks reuse them across every input/output they inspect.
    /// </summary>
    Task<PayjoinScriptOwnershipResolver> CreateResolverAsync(string storeId, byte[] receiverScript, CancellationToken cancellationToken);
}

internal sealed class PayjoinWalletOwnershipService : IPayjoinWalletOwnershipService
{
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly ExplorerClientProvider _explorerClientProvider;

    public PayjoinWalletOwnershipService(
        BTCPayNetworkProvider networkProvider,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        ExplorerClientProvider explorerClientProvider)
    {
        _networkProvider = networkProvider;
        _storeRepository = storeRepository;
        _handlers = handlers;
        _explorerClientProvider = explorerClientProvider;
    }

    public async Task<PayjoinScriptOwnershipResolver> CreateResolverAsync(string storeId, byte[] receiverScript, CancellationToken cancellationToken)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>(PayjoinConstants.BitcoinCode)
            ?? throw new InvalidOperationException($"Network '{PayjoinConstants.BitcoinCode}' is not available.");
        var store = await _storeRepository.FindStore(storeId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Store {storeId} not found");
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(PayjoinConstants.BitcoinCode);
        var derivationScheme = store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, _handlers, true)
            ?? throw new InvalidOperationException($"Derivation scheme not configured for {PayjoinConstants.BitcoinCode}");
        var client = _explorerClientProvider.GetExplorerClient(network);

        return new PayjoinScriptOwnershipResolver(client, derivationScheme.AccountDerivation, receiverScript);
    }
}

/// <summary>
/// Resolves script ownership against a single store's derivation scheme. The invoice's own receiver
/// script is always treated as owned; any other script is checked against the wallet via NBXplorer.
/// </summary>
internal sealed class PayjoinScriptOwnershipResolver
{
    private readonly ExplorerClient _client;
    private readonly NBXplorer.DerivationStrategy.DerivationStrategyBase _accountDerivation;
    private readonly byte[] _receiverScript;

    internal PayjoinScriptOwnershipResolver(
        ExplorerClient client,
        NBXplorer.DerivationStrategy.DerivationStrategyBase accountDerivation,
        byte[] receiverScript)
    {
        _client = client;
        _accountDerivation = accountDerivation;
        _receiverScript = receiverScript;
    }

    public bool IsOwned(byte[] scriptBytes)
    {
        if (scriptBytes.AsSpan().SequenceEqual(_receiverScript))
        {
            return true;
        }

        var script = Script.FromBytesUnsafe(scriptBytes);
        // The rust-payjoin ownership callbacks are synchronous; this runs on the background poller thread
        // (no synchronization context), so blocking on the NBXplorer lookup is safe.
        var keyInformation = _client.GetKeyInformationAsync(_accountDerivation, script).ConfigureAwait(false).GetAwaiter().GetResult();
        return keyInformation is not null;
    }
}
