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
using Microsoft.Extensions.Logging;
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
    private static readonly Action<ILogger, string, string, Exception?> LogPayjoinSenderBroadcasted =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1, nameof(LogPayjoinSenderBroadcasted)),
            "Payjoin sender broadcasted payjoin transaction {TransactionId} for {InvoiceId}");

    private readonly BTCPayServerEnvironment _env;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayjoinStoreSettingsRepository _storeSettingsRepository;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly ILogger<UIPayJoinController>? _logger;

    public UIPayJoinController(
        BTCPayServerEnvironment env,
        InvoiceRepository invoiceRepository,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        BTCPayNetworkProvider networkProvider,
        IHttpClientFactory httpClientFactory,
        IPayjoinStoreSettingsRepository storeSettingsRepository,
        ExplorerClientProvider explorerClientProvider,
        BTCPayWalletProvider walletProvider,
        ILogger<UIPayJoinController>? logger = null)
    {
        _env = env;
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
        _handlers = handlers;
        _networkProvider = networkProvider;
        _httpClientFactory = httpClientFactory;
        _storeSettingsRepository = storeSettingsRepository;
        _explorerClientProvider = explorerClientProvider;
        _walletProvider = walletProvider;
        _logger = logger;
    }

    // TODO: Remove this test endpoint.
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("run-test-payment")]
    public async Task<ActionResult<RunTestPaymentResponse>> RunTestPayment([FromBody] RunTestPaymentRequest request, CancellationToken cancellationToken)
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
            return RunTestPaymentFailure("invoiceId is required");
        }

        if (request.PaymentUrl is null)
        {
            return RunTestPaymentFailure("paymentUrl is required");
        }

        var invoice = await _invoiceRepository.GetInvoice(request.InvoiceId).ConfigureAwait(false);
        if (invoice is null)
        {
            return RunTestPaymentFailure("invoice not found");
        }

        var store = await _storeRepository.FindStore(invoice.StoreId).ConfigureAwait(false);
        if (store is null)
        {
            return RunTestPaymentFailure("store not found");
        }

        var storeSettings = await _storeSettingsRepository.GetAsync(invoice.StoreId).ConfigureAwait(false);
        if (storeSettings is null || storeSettings.OhttpRelayUrl is null)
        {
            return RunTestPaymentFailure("OhttpRelayUrl not found");
        }

        var cryptoCode = "BTC";
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        var derivationScheme = store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, _handlers, true);
        if (derivationScheme is null)
        {
            return RunTestPaymentFailure("wallet not configured");
        }

        var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network is null)
        {
            return RunTestPaymentFailure("network not available");
        }

        var wallet = _walletProvider.GetWallet(network);
        if (wallet is null)
        {
            return RunTestPaymentFailure("wallet not available");
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
                return RunTestPaymentFailure("payment amount missing in paymentUrl");
            }

            paymentAmount = Money.Satoshis(checked((long)amountSats.Value)).ToDecimal(MoneyUnit.BTC);
            pjUri = parsedUri.CheckPjSupported();
        }
        catch (PjParseException ex)
        {
            return RunTestPaymentFailure($"Invalid BIP21 URI: {ex.Message}");
        }
        catch (PjNotSupported ex)
        {
            return RunTestPaymentFailure($"Payjoin not available in URI: {ex.Message}");
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
            return RunTestPaymentFailure("wallet funds too low for fees");
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
            return RunTestPaymentFailure(ex.Message);
        }

        if (createResult?.PSBT is null)
        {
            return RunTestPaymentFailure("psbt creation failed");
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
                        var outcome = pollTransition.Save(senderPersister);

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
                return RunTestPaymentFailure($"Sender build failed: {ex.Message}");
            }
            catch (SenderPersistedException.ResponseException ex)
            {
                return RunTestPaymentFailure($"Sender rejected by receiver: {FormatSenderResponseException(ex.v1)}");
            }
            catch (InvalidOperationException ex)
            {
                return RunTestPaymentFailure($"Sender failed: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                return RunTestPaymentFailure($"Sender failed: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                return RunTestPaymentFailure($"Sender failed: {ex.Message}");
            }
            catch (SenderPersistedException ex)
            {
                return RunTestPaymentFailure($"Sender state failed: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(proposalPsbtBase64))
        {
            return RunTestPaymentFailure("No payjoin proposal yet");
        }

        PSBT proposalPsbt;
        try
        {
            proposalPsbt = PSBT.Parse(proposalPsbtBase64, network.NBitcoinNetwork);
        }
        catch (FormatException ex)
        {
            return RunTestPaymentFailure($"Invalid PSBT: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return RunTestPaymentFailure($"Invalid PSBT: {ex.Message}");
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
            return RunTestPaymentFailure(error);
        }

        var signingKey = ExtKey.Parse(signingKeyStr, network.NBitcoinNetwork);
        var signingKeySettings = derivationScheme.GetAccountKeySettingsFromRoot(signingKey);
        var rootedKeyPath = signingKeySettings?.GetRootedKeyPath();
        if (rootedKeyPath is null || signingKeySettings is null)
        {
            return RunTestPaymentFailure("wallet key path mismatch");
        }

        proposalPsbt.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
        var accountKey = signingKey.Derive(rootedKeyPath.KeyPath);
        proposalPsbt.SignAll(derivationScheme.AccountDerivation, accountKey, rootedKeyPath);

        if (!proposalPsbt.TryFinalize(out _))
        {
            return RunTestPaymentFailure("PSBT could not be finalized");
        }

        var transaction = proposalPsbt.ExtractTransaction();
        var broadcast = await client.BroadcastAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!broadcast.Success)
        {
            return RunTestPaymentFailure($"Broadcast failed: {broadcast.RPCCode} {broadcast.RPCCodeMessage} {broadcast.RPCMessage}");
        }

        if (_logger is not null)
        {
            LogPayjoinSenderBroadcasted(_logger, transaction.GetHash().ToString(), request.InvoiceId, null);
        }

        if (_env.CheatMode)
        {
            var rpc = client.RPCClient;
            if (rpc is not null)
            {
                var rewardAddress = await rpc.GetNewAddressAsync(cancellationToken).ConfigureAwait(false);
                await rpc.GenerateToAddressAsync(1, rewardAddress, cancellationToken).ConfigureAwait(false);
            }
        }

        var txid = transaction.GetHash().ToString();
        return RunTestPaymentSuccess($"Payjoin transaction broadcasted: {txid}", txid);
    }

    private static string FormatSenderResponseException(global::Payjoin.ResponseException responseException)
    {
        return responseException switch
        {
            global::Payjoin.ResponseException.WellKnown wellKnown => wellKnown.v1.ToString() ?? nameof(global::Payjoin.ResponseException.WellKnown),
            global::Payjoin.ResponseException.Validation validation => validation.v1.ToString() ?? nameof(global::Payjoin.ResponseException.Validation),
            global::Payjoin.ResponseException.Unrecognized unrecognized => $"{unrecognized.errorCode}: {unrecognized.msg}",
            _ => responseException.ToString() ?? responseException.GetType().Name
        };
    }

    private OkObjectResult RunTestPaymentFailure(string message)
    {
        return Ok(RunTestPaymentResponse.Failure(message));
    }

    private OkObjectResult RunTestPaymentSuccess(string message, string transactionId)
    {
        return Ok(RunTestPaymentResponse.Success(message, transactionId));
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
