using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Plugins.Payjoin.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using Payjoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static Payjoin.SenderPersistedException;
using PayjoinUri = Payjoin.Uri;

namespace BTCPayServer.Plugins.Payjoin.Controllers;

[Route("~/plugins/payjoin")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class UIPayJoinController : Controller
{
    private readonly BTCPayServerEnvironment _env;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PayjoinDemoContext _demoContext;
    private readonly PayjoinReceiverSessionStore _receiverSessionStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayjoinStoreSettingsRepository _storeSettingsRepository;
    private readonly PayjoinBip21Service _bip21Service;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayWalletProvider _walletProvider;

    public UIPayJoinController(
        BTCPayServerEnvironment env,
        InvoiceRepository invoiceRepository,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        BTCPayNetworkProvider networkProvider,
        PayjoinDemoContext demoContext,
        PayjoinReceiverSessionStore receiverSessionStore,
        IHttpClientFactory httpClientFactory,
        IPayjoinStoreSettingsRepository storeSettingsRepository,
        PayjoinBip21Service bip21Service,
        ExplorerClientProvider explorerClientProvider,
        BTCPayWalletProvider walletProvider)
    {
        _env = env;
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
        _handlers = handlers;
        _networkProvider = networkProvider;
        _demoContext = demoContext;
        _receiverSessionStore = receiverSessionStore;
        _httpClientFactory = httpClientFactory;
        _storeSettingsRepository = storeSettingsRepository;
        _bip21Service = bip21Service;
        _explorerClientProvider = explorerClientProvider;
        _walletProvider = walletProvider;
    }

    [AllowAnonymous]
    [HttpGet("invoices/{invoiceId}/bip21")]
    public async Task<IActionResult> GetBip21(string invoiceId, string mode = "standard", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return BadRequest(new { error = "invoiceId is required" });
        }

        var invoice = await _invoiceRepository.GetInvoice(invoiceId).ConfigureAwait(false);
        if (invoice is null)
        {
            return NotFound();
        }

        var cryptoCode = "BTC";
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        var prompt = invoice.GetPaymentPrompt(paymentMethodId);
        if (prompt?.Destination is null)
        {
            return BadRequest(new { error = "BTC payment prompt not available" });
        }

        var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            return BadRequest(new { error = "BTC network not available" });
        }

        var storeSettings = await _storeSettingsRepository.GetAsync(invoice.StoreId).ConfigureAwait(false);

        var enablePayjoin = storeSettings?.EnabledByDefault ?? false;

        var calculation = prompt.Calculate();
        var bip21 = await _bip21Service.BuildAsync(
            cryptoCode,
            prompt.Destination,
            calculation.Due,
            storeSettings,
            enablePayjoin,
            invoice.Id,
            invoice.StoreId,
            cancellationToken).ConfigureAwait(false);

        var actualPayjoinEnabled = IsPayjoinEnabled(bip21);
        return Ok(new { bip21, payjoinEnabled = actualPayjoinEnabled });
    }

    private static bool IsPayjoinEnabled(string paymentUrl)
    {
        try
        {
            using var parsedUri = PayjoinUri.Parse(paymentUrl);
            using var _ = parsedUri.CheckPjSupported();
            return true;
        }
        catch (PjParseException)
        {
            return false;
        }
        catch (PjNotSupported)
        {
            return false;
        }
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("run-test-payment")]
    public async Task<IActionResult> RunTestPayment([FromBody] RunTestPaymentRequest request, CancellationToken cancellationToken)
    {
        if (!_env.CheatMode)
        {
            return NotFound();
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.InvoiceId))
        {
            return Ok(new { errorMessage = "invoiceId is required" });
        }

        if (request.PaymentUrl is null)
        {
            return Ok(new { errorMessage = "paymentUrl is required" });
        }

        var invoice = await _invoiceRepository.GetInvoice(request.InvoiceId).ConfigureAwait(false);
        if (invoice is null)
        {
            return Ok(new { errorMessage = "invoice not found" });
        }

        var store = await _storeRepository.FindStore(invoice.StoreId).ConfigureAwait(false);
        if (store is null)
        {
            return Ok(new { errorMessage = "store not found" });
        }

        var storeSettings = await _storeSettingsRepository.GetAsync(invoice.StoreId).ConfigureAwait(false);
        if (storeSettings is null || storeSettings.OhttpRelayUrl is null)
        {
            return Ok(new { errorMessage = "OhttpRelayUrl not found" });
        }

        var cryptoCode = "BTC";
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        var derivationScheme = store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, _handlers, true);
        if (derivationScheme is null)
        {
            return Ok(new { errorMessage = "wallet not configured" });
        }

        var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            return Ok(new { errorMessage = "network not available" });
        }

        var wallet = _walletProvider.GetWallet(network);
        if (wallet is null)
        {
            return Ok(new { errorMessage = "wallet not available" });
        }

        var confirmedCoins = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation, true, cancellationToken).ConfigureAwait(false);
        var allCoins = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation, false, cancellationToken).ConfigureAwait(false);
        var confirmedTotal = confirmedCoins.Sum(coin => coin.Value.GetValue(network));
        var allTotal = allCoins.Sum(coin => coin.Value.GetValue(network));

        PjUri pjUri;
        string paymentAddress;
        decimal paymentAmount;
        try
        {
            using var parsedUri = PayjoinUri.Parse(request.PaymentUrl.ToString());
            paymentAddress = parsedUri.Address();
            var amountSats = parsedUri.AmountSats();
            if (amountSats is null)
            {
                return Ok(new { errorMessage = "payment amount missing in paymentUrl" });
            }

            paymentAmount = Money.Satoshis(checked((long)amountSats.Value)).ToDecimal(MoneyUnit.BTC);
            pjUri = parsedUri.CheckPjSupported();
        }
        catch (PjParseException ex)
        {
            return Ok(new { errorMessage = $"Invalid BIP21 URI: {ex.Message}" });
        }
        catch (PjNotSupported ex)
        {
            return Ok(new { errorMessage = $"Payjoin not available in URI: {ex.Message}" });
        }

        var psbtRequest = new CreatePSBTRequest
        {
            RBF = network.SupportRBF ? true : null,
            FeePreference = new FeePreference
            {
                ExplicitFeeRate = new FeeRate(1.0m)
            }
        };

        var available = confirmedTotal < paymentAmount && allTotal >= paymentAmount ? allTotal : confirmedTotal;
        var feeBuffer = Money.Satoshis(1000).ToDecimal(MoneyUnit.BTC);
        var spendable = Math.Max(available - feeBuffer, 0.0m);
        if (spendable <= 0.0m)
        {
            return Ok(new { errorMessage = "wallet funds too low for fees" });
        }

        var amountToSend = Math.Min(paymentAmount, spendable);
        psbtRequest.Destinations.Add(new CreatePSBTDestination
        {
            Destination = BitcoinAddress.Create(paymentAddress, network.NBitcoinNetwork),
            Amount = Money.Coins(amountToSend)
        });

        if (confirmedTotal < paymentAmount && allTotal >= paymentAmount && allCoins.Length > 0)
        {
            psbtRequest.IncludeOnlyOutpoints = allCoins.Select(coin => coin.OutPoint).ToList();
        }

        var client = _explorerClientProvider.GetExplorerClient(network);
        CreatePSBTResponse? createResult;
        try
        {
            createResult = await client.CreatePSBTAsync(derivationScheme.AccountDerivation, psbtRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (NBXplorerException ex)
        {
            return Ok(new { errorMessage = ex.Message });
        }

        if (createResult?.PSBT is null)
        {
            return Ok(new { errorMessage = "psbt creation failed" });
        }

        var senderPsbtBase64 = createResult.PSBT.ToBase64();
        string? proposalPsbtBase64 = null;

        using (pjUri)
        {
            try
            {
                var senderPersister = new InMemorySenderPersister();
                using var senderBuilder = new SenderBuilder(senderPsbtBase64, pjUri);
                using var initial = senderBuilder.BuildRecommended(250);
                using var withReplyKey = initial.Save(senderPersister);

                using var postContext = withReplyKey.CreateV2PostRequest(storeSettings.OhttpRelayUrl.ToString());
                var postResponse = await SendRequestAsync(postContext.request, cancellationToken).ConfigureAwait(false);
                using var withReplyTransition = withReplyKey.ProcessResponse(postResponse, postContext.ohttpCtx);

                var current = withReplyTransition.Save(senderPersister);
                try
                {
                    for (var attempt = 0; attempt < 5; attempt++)
                    {
                        using var pollRequest = current.CreatePollRequest(storeSettings.OhttpRelayUrl.ToString());
                        var pollResponse = await SendRequestAsync(pollRequest.request, cancellationToken).ConfigureAwait(false);
                        using var pollTransition = current.ProcessResponse(pollResponse, pollRequest.ohttpCtx);
                        PollingForProposalTransitionOutcome? outcome;
                        try
                        {
                            outcome = pollTransition.Save(senderPersister);
                        }
                        catch (SenderPersistedException.ResponseException)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        try
                        {
                            switch (outcome)
                            {
                                case PollingForProposalTransitionOutcome.Progress progress:
                                    proposalPsbtBase64 = progress.psbtBase64;
                                    break;
                                case PollingForProposalTransitionOutcome.Stasis stasis:
                                    current.Dispose();
                                    current = stasis.inner;
                                    outcome = null;
                                    break;
                            }
                        }
                        finally
                        {
                            outcome?.Dispose();
                        }

                        if (proposalPsbtBase64 is not null)
                        {
                            break;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    current.Dispose();
                }
            }
            catch (BuildSenderException ex)
            {
                return Ok(new { errorMessage = $"Sender build failed: {ex.Message}" });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new { errorMessage = $"Sender failed: {ex.Message}" });
            }
            catch (HttpRequestException ex)
            {
                return Ok(new { errorMessage = $"Sender failed: {ex.Message}" });
            }
            catch (TaskCanceledException ex)
            {
                return Ok(new { errorMessage = $"Sender failed: {ex.Message}" });
            }
            catch (SenderPersistedException.ResponseException ex)
            {
                return Ok(new { errorMessage = $"Sender response invalid: {ex.Message}" });
            }
            catch (SenderPersistedException ex)
            {
                return Ok(new { errorMessage = $"Sender state failed: {ex.Message}" });
            }
        }

        if (string.IsNullOrWhiteSpace(proposalPsbtBase64))
        {
            return Ok(new { errorMessage = "No payjoin proposal yet" });
        }

        PSBT proposalPsbt;
        try
        {
            proposalPsbt = PSBT.Parse(proposalPsbtBase64, network.NBitcoinNetwork);
        }
        catch (FormatException ex)
        {
            return Ok(new { errorMessage = $"Invalid PSBT: {ex.Message}" });
        }
        catch (ArgumentException ex)
        {
            return Ok(new { errorMessage = $"Invalid PSBT: {ex.Message}" });
        }

        derivationScheme.RebaseKeyPaths(proposalPsbt);
        var signingKeyStr = await client.GetMetadataAsync<string>(
            derivationScheme.AccountDerivation,
            WellknownMetadataKeys.MasterHDKey,
            cancellationToken).ConfigureAwait(false);
        if (!derivationScheme.IsHotWallet || signingKeyStr is null)
        {
            var error = !derivationScheme.IsHotWallet
                ? "cannot sign from a cold wallet"
                : "wallet seed not available";
            return Ok(new { errorMessage = error });
        }

        var signingKey = ExtKey.Parse(signingKeyStr, network.NBitcoinNetwork);
        var signingKeySettings = derivationScheme.GetAccountKeySettingsFromRoot(signingKey);
        var rootedKeyPath = signingKeySettings?.GetRootedKeyPath();
        if (rootedKeyPath is null || signingKeySettings is null)
        {
            return Ok(new { errorMessage = "wallet key path mismatch" });
        }

        proposalPsbt.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
        var accountKey = signingKey.Derive(rootedKeyPath.KeyPath);
        proposalPsbt.SignAll(derivationScheme.AccountDerivation, accountKey, rootedKeyPath);

        if (!proposalPsbt.TryFinalize(out _))
        {
            return Ok(new { errorMessage = "PSBT could not be finalized" });
        }

        var transaction = proposalPsbt.ExtractTransaction();
        var broadcast = await client.BroadcastAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!broadcast.Success)
        {
            return Ok(new { errorMessage = $"Broadcast failed: {broadcast.RPCCode} {broadcast.RPCCodeMessage} {broadcast.RPCMessage}" });
        }

        return Ok(new { successMessage = $"Payjoin transaction broadcasted: {transaction.GetHash()}" });
    }

    private async Task<byte[]> SendRequestAsync(Request request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, request.url)
        {
            Content = new ByteArrayContent(request.body)
        };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.contentType);

        var client = _httpClientFactory.CreateClient(nameof(UIPayJoinController));
        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class InMemorySenderPersister : JsonSenderSessionPersister
    {
        private readonly List<string> _events = new();

        public void Save(string @event) => _events.Add(@event);

        public string[] Load() => _events.ToArray();

        public void Close()
        {
        }
    }
}
