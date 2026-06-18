using BTCPayServer.Services.Wallets;
using Payjoin;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Services;

internal interface IPayjoinReceiverProposalSigner
{
    // Resolves the hot-wallet account key (async) and returns a synchronous signer for rust-payjoin's
    // finalize_proposal callback, which signs only the receiver's contributed inputs on the library-supplied
    // PSBT. The library handles sender-signature removal and field filtering, so no manual stripping is done.
    Task<ProcessPsbt> CreateContributedInputSignerAsync(
        string storeId,
        ReceivedCoin[] receiverCoins,
        CancellationToken cancellationToken);
}
