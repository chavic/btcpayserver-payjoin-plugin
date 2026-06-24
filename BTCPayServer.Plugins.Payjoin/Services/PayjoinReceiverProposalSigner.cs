using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using NBitcoin;
using NBXplorer;
using Payjoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Services;

internal sealed class PayjoinReceiverProposalSigner : IPayjoinReceiverProposalSigner
{
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly ExplorerClientProvider _explorerClientProvider;

    public PayjoinReceiverProposalSigner(
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

    public async Task<ProcessPsbt> CreateContributedInputSignerAsync(
        string storeId,
        ReceivedCoin[] receiverCoins,
        CancellationToken cancellationToken)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>(PayjoinConstants.BitcoinCode) ?? throw new InvalidOperationException("BTC network not available");
        var store = await _storeRepository.FindStore(storeId).ConfigureAwait(false) ?? throw new InvalidOperationException($"Store {storeId} not found");
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(PayjoinConstants.BitcoinCode);
        var derivationScheme = store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, _handlers, true) ?? throw new InvalidOperationException("Derivation scheme not configured for BTC");

        if (!derivationScheme.IsHotWallet)
        {
            throw new InvalidOperationException("Cannot sign payjoin proposal from a cold wallet");
        }

        var client = _explorerClientProvider.GetExplorerClient(network);
        var signingKeyStr = await client.GetMetadataAsync<string>(
            derivationScheme.AccountDerivation,
            WellknownMetadataKeys.MasterHDKey,
            cancellationToken).ConfigureAwait(false);

        if (signingKeyStr is null)
        {
            throw new InvalidOperationException("Wallet seed not available");
        }

        var signingKey = ExtKey.Parse(signingKeyStr, network.NBitcoinNetwork);
        var signingKeySettings = derivationScheme.GetAccountKeySettingsFromRoot(signingKey) ?? throw new InvalidOperationException("Wallet key settings not available");
        var rootedKeyPath = signingKeySettings.GetRootedKeyPath() ?? throw new InvalidOperationException("Wallet key path mismatch");
        var accountKey = signingKey.Derive(rootedKeyPath.KeyPath);

        return new ContributedInputSigner(network.NBitcoinNetwork, accountKey, receiverCoins);
    }

    public static void EnsureContributedInputsPresent(PSBT proposalPsbt, ReceivedCoin[] receiverCoins)
    {
        var missingInputs = receiverCoins
            .Where(receiverCoin => proposalPsbt.Inputs.All(input => input.PrevOut != receiverCoin.OutPoint))
            .Select(receiverCoin => receiverCoin.OutPoint.ToString())
            .ToArray();

        if (missingInputs.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException($"Provisional proposal is missing contributed receiver inputs: {string.Join(", ", missingInputs)}");
    }

    // Signs only the receiver's contributed inputs on the PSBT that rust-payjoin hands to the wallet during
    // finalize_proposal. That PSBT already has the sender's now-invalid signatures cleared by the library
    // (psbt_to_sign), and the library's prepare_psbt drops partial signatures and key paths afterwards. So the
    // receiver never signs a foreign input and no manual stripping is required. The account key is resolved
    // ahead of time (CreateContributedInputSignerAsync) because this callback is synchronous.
    internal sealed class ContributedInputSigner : ProcessPsbt
    {
        private readonly Network _network;
        private readonly ExtKey _accountKey;
        private readonly ReceivedCoin[] _receiverCoins;

        public ContributedInputSigner(Network network, ExtKey accountKey, ReceivedCoin[] receiverCoins)
        {
            _network = network;
            _accountKey = accountKey;
            _receiverCoins = receiverCoins;
        }

        public string Callback(string psbt)
        {
            var proposalPsbt = PSBT.Parse(psbt, _network);
            EnsureContributedInputsPresent(proposalPsbt, _receiverCoins);

            foreach (var coin in _receiverCoins)
            {
                var contributedInput = proposalPsbt.Inputs.FindIndexedInput(coin.OutPoint)!;

                contributedInput.UpdateFromCoin(coin.Coin);
                contributedInput.Sign(_accountKey.Derive(coin.KeyPath).PrivateKey);
                if (!contributedInput.TryFinalizeInput(out _))
                {
                    throw new InvalidOperationException($"Receiver input '{coin.OutPoint}' could not be finalized.");
                }
            }

            return proposalPsbt.ToBase64();
        }
    }
}
