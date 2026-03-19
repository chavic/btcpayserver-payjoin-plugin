using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Payjoin.Controllers;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Plugins.Payjoin.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Payjoin.IntegrationTests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class PayjoinPluginIntegrationTests : UnitTestBase
{
    private const string BitcoinCode = "BTC";
    private const decimal InvoicePrice = 0.1m;
    private static readonly Money InitialWalletFunding = Money.Coins(1.0m);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    public PayjoinPluginIntegrationTests(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact
    (Skip = "Manual Docker-backed integration test. Remove Skip to run it explicitly.")
    ]
    [Trait("Integration", "Integration")]
    public async Task CreateInvoiceAndPayItThroughThePayjoinPlugin()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        using var tester = CreateServerTester(newDb: true);
        var context = await CreateInitializedTestContextAsync(tester).ConfigureAwait(true);

        await UpdateStoreSettingsAsync(tester, context.User.StoreId, new PayjoinStoreSettings
        {
            EnabledByDefault = true
        }).ConfigureAwait(true);

        var paymentResult = await CreateAndPayInvoiceViaPayjoinAsync(tester, context.User, context.Network, cts.Token).ConfigureAwait(true);

        Assert.Equal(uint256.Parse(paymentResult.TransactionId), paymentResult.PayjoinTransaction.GetHash());
        Assert.True(paymentResult.PayjoinTransaction.Inputs.Count > 1,
            $"Expected payjoin tx to contain multiple inputs. Inputs: {paymentResult.PayjoinTransaction.Inputs.Count}");

        var invoiceOutputs = paymentResult.PayjoinTransaction.Outputs
            .Where(output => output.ScriptPubKey == paymentResult.InvoiceScript)
            .ToList();
        Assert.Single(invoiceOutputs);
    }

    [Fact
    (Skip = "Manual Docker-backed integration test. Remove Skip to run it explicitly.")
    ]
    [Trait("Integration", "Integration")]
    public async Task PayjoinChangeOutputGoesToColdWallet()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        using var tester = CreateServerTester(newDb: true);
        var context = await CreateInitializedTestContextAsync(tester).ConfigureAwait(true);

        var coldKey = new ExtKey();
        var coldAccountKey = coldKey.Derive(new KeyPath("m/84'/1'/0'"));
        var factory = tester.ExplorerClient.Network.DerivationStrategyFactory;
        var coldDerivation = factory.CreateDirectDerivationStrategy(
            coldAccountKey.Neuter(),
            new DerivationStrategyOptions { ScriptPubKeyType = ScriptPubKeyType.Segwit });

        await tester.ExplorerClient.TrackAsync(coldDerivation).ConfigureAwait(true);

        var coldUtxosBefore = await tester.ExplorerClient.GetUTXOsAsync(coldDerivation).ConfigureAwait(true);
        Assert.Empty(coldUtxosBefore.GetUnspentUTXOs());

        await UpdateStoreSettingsAsync(tester, context.User.StoreId, new PayjoinStoreSettings
        {
            EnabledByDefault = true,
            ColdWalletDerivationScheme = coldDerivation.ToString()
        }).ConfigureAwait(true);

        var paymentResult = await CreateAndPayInvoiceViaPayjoinAsync(tester, context.User, context.Network, cts.Token).ConfigureAwait(true);

        var explorerClient = tester.PayTester.GetService<ExplorerClientProvider>().GetExplorerClient(context.Network);
        var coldWalletUtxos = await explorerClient.GetUTXOsAsync(coldDerivation).ConfigureAwait(true);
        Assert.NotNull(coldWalletUtxos);

        var coldUtxo = Assert.Single(coldWalletUtxos.GetUnspentUTXOs());
        Assert.NotEqual(paymentResult.InvoiceScript, coldUtxo.ScriptPubKey);
        Assert.True((Money)coldUtxo.Value > Money.Zero, "Cold wallet UTXO should have positive value");
        var coldTxOutput = paymentResult.PayjoinTransaction.Outputs.FirstOrDefault(o => o.ScriptPubKey == coldUtxo.ScriptPubKey);
        Assert.NotNull(coldTxOutput);
        Assert.Equal((Money)coldUtxo.Value, coldTxOutput.Value);
    }

    private static void AssertHasReceiverContribution(Transaction payjoinTx, HashSet<string> receiverOutpointsBeforePayment)
    {
        var receiverContributionFound = payjoinTx.Inputs.Any(input => receiverOutpointsBeforePayment.Contains(input.PrevOut.ToString()));
        Assert.True(receiverContributionFound,
            $"Expected payjoin tx to spend at least one receiver-owned coin. Inputs: {string.Join(", ", payjoinTx.Inputs.Select(input => input.PrevOut))}");
    }

    private static async Task<TestContext> CreateInitializedTestContextAsync(ServerTester tester)
    {
        await tester.StartAsync().ConfigureAwait(true);

        var network = GetBitcoinNetwork(tester);
        var user = tester.NewAccount();
        await user.GrantAccessAsync().ConfigureAwait(true);
        await user.RegisterDerivationSchemeAsync(BitcoinCode, ScriptPubKeyType.Segwit, true).ConfigureAwait(true);
        await FundWalletAsync(user, network).ConfigureAwait(true);

        return new TestContext(network, user);
    }

    private static BTCPayNetwork GetBitcoinNetwork(ServerTester tester)
    {
        var network = tester.NetworkProvider.GetNetwork<BTCPayNetwork>(BitcoinCode);
        Assert.NotNull(network);
        return network;
    }

    private static async Task FundWalletAsync(TestAccount user, BTCPayNetwork network)
    {
        await user.ReceiveUTXO(InitialWalletFunding, network).ConfigureAwait(true);
        await user.ReceiveUTXO(InitialWalletFunding, network).ConfigureAwait(true);
    }

    private static async Task UpdateStoreSettingsAsync(ServerTester tester, string storeId, PayjoinStoreSettings settings)
    {
        var storeSettingsRepository = tester.PayTester.GetService<IPayjoinStoreSettingsRepository>();
        await storeSettingsRepository.SetAsync(storeId, settings).ConfigureAwait(true);
    }

    private static PaymentMethodId GetBitcoinPaymentMethodId()
    {
        return PaymentTypes.CHAIN.GetPaymentMethodId(BitcoinCode);
    }

    private static async Task<(Transaction PayjoinTransaction, Script InvoiceScript, string TransactionId)> CreateAndPayInvoiceViaPayjoinAsync(
        ServerTester tester,
        TestAccount user,
        BTCPayNetwork network,
        CancellationToken cancellationToken)
    {
        var invoice = user.BitPay.CreateInvoice(new Invoice
        {
            Price = InvoicePrice,
            Currency = BitcoinCode,
            FullNotifications = true
        });

        var paymentMethodId = GetBitcoinPaymentMethodId();
        var invoiceBeforePayment = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice.Id).ConfigureAwait(true);
        Assert.NotNull(invoiceBeforePayment);

        var promptBeforePayment = invoiceBeforePayment.GetPaymentPrompt(paymentMethodId);
        Assert.NotNull(promptBeforePayment);

        var expectedDue = promptBeforePayment.Calculate().Due;
        var receiverOutpointsBeforePayment = await GetReceiverOutpointsAsync(tester, user.StoreId, cancellationToken).ConfigureAwait(true);

        var controller = tester.PayTester.GetController<UIPayJoinController>();
        var bip21Result = Assert.IsType<OkObjectResult>(await controller.GetBip21(invoice.Id, cancellationToken: cancellationToken).ConfigureAwait(true));
        var bip21Response = Assert.IsType<GetBip21Response>(bip21Result.Value);
        Assert.True(bip21Response.PayjoinEnabled);

        var paymentUrl = new Uri(bip21Response.Bip21, UriKind.Absolute);
        Assert.True(paymentUrl.Query.Contains("pj=", StringComparison.OrdinalIgnoreCase),
            $"Expected BIP21 URI to contain pj=, got '{bip21Response.Bip21}'");
        var paymentActionResult = await controller.RunTestPayment(new RunTestPaymentRequest
        {
            InvoiceId = invoice.Id,
            PaymentUrl = paymentUrl
        }, cancellationToken).ConfigureAwait(true);
        var paymentResult = Assert.IsType<OkObjectResult>(paymentActionResult.Result);
        var paymentResponse = Assert.IsType<RunTestPaymentResponse>(paymentResult.Value);
        Assert.True(paymentResponse.Succeeded, paymentResponse.Message);
        Assert.False(string.IsNullOrWhiteSpace(paymentResponse.TransactionId), "TransactionId must be returned on success");

        var payjoinTx = await GetPayjoinTransactionAsync(tester, paymentResponse.TransactionId).ConfigureAwait(true);
        AssertHasReceiverContribution(payjoinTx, receiverOutpointsBeforePayment);

        var invoiceScript = BitcoinAddress.Create(promptBeforePayment.Destination, network.NBitcoinNetwork).ScriptPubKey;
        var invoiceOutput = payjoinTx.Outputs.FirstOrDefault(o => o.ScriptPubKey == invoiceScript);
        Assert.NotNull(invoiceOutput);
        Assert.Equal(Money.Coins(expectedDue), invoiceOutput.Value);

        await user.WaitInvoicePaid(invoice.Id).ConfigureAwait(true);

        var invoiceEntity = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice.Id).ConfigureAwait(true);
        Assert.NotNull(invoiceEntity);

        var payments = invoiceEntity
            .GetPayments(false)
            .Where(p => p.Accounted && p.PaymentMethodId == paymentMethodId)
            .ToList();
        Assert.Single(payments);

        var totalPaid = payments.Sum(p => p.Value);
        Assert.Equal(expectedDue, totalPaid);

        return (payjoinTx, invoiceScript, paymentResponse.TransactionId!);
    }

    private static async Task<HashSet<string>> GetReceiverOutpointsAsync(ServerTester tester, string storeId, CancellationToken cancellationToken)
    {
        var network = GetBitcoinNetwork(tester);

        var store = await tester.PayTester.GetService<StoreRepository>().FindStore(storeId).ConfigureAwait(true);
        Assert.NotNull(store);

        var paymentMethodId = GetBitcoinPaymentMethodId();
        var handlers = tester.PayTester.GetService<PaymentMethodHandlerDictionary>();
        var derivationScheme = store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers, true);
        Assert.NotNull(derivationScheme);

        var wallet = tester.PayTester.GetService<BTCPayWalletProvider>().GetWallet(network);
        Assert.NotNull(wallet);

        var confirmedCoins = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation, true, cancellationToken).ConfigureAwait(true);
        var allCoins = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation, false, cancellationToken).ConfigureAwait(true);
        var receiverCoins = confirmedCoins.Length > 0 ? confirmedCoins : allCoins;

        Assert.NotEmpty(receiverCoins);
        return receiverCoins
            .Select(coin => coin.OutPoint.ToString())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<Transaction> GetPayjoinTransactionAsync(ServerTester tester, string transactionId)
    {
        var bestBlock = await tester.ExplorerNode.GetBestBlockHashAsync().ConfigureAwait(true);
        return await tester.ExplorerNode.GetRawTransactionAsync(uint256.Parse(transactionId), bestBlock).ConfigureAwait(true);
    }

    private sealed record TestContext(BTCPayNetwork Network, TestAccount User);
}
