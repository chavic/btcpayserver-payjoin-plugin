#nullable enable

using BTCPayServer.Plugins.Payjoin.Controllers;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Plugins.Payjoin.Services;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Payjoin.IntegrationTests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class PayjoinPluginIntegrationTests : UnitTestBase
{
    public PayjoinPluginIntegrationTests(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact]
    [Trait("Integration", "Integration")]
    public async Task CreateInvoiceAndPayItThroughThePayjoinPlugin()
    {
        using var tester = CreateServerTester(newDb: true);
        await tester.StartAsync();

        var user = tester.NewAccount();
        await user.GrantAccessAsync();
        await user.RegisterDerivationSchemeAsync("BTC", ScriptPubKeyType.Segwit, true);
        await user.ReceiveUTXO(Money.Coins(1.0m), tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
        await user.ReceiveUTXO(Money.Coins(1.0m), tester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));

        var storeSettingsRepository = tester.PayTester.GetService<IPayjoinStoreSettingsRepository>();
        await storeSettingsRepository.SetAsync(user.StoreId, new PayjoinStoreSettings
        {
            EnabledByDefault = true
        });

        var invoice = user.BitPay.CreateInvoice(new Invoice
        {
            Price = 0.1m,
            Currency = "BTC",
            FullNotifications = true
        });

        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
        var invoiceBeforePayment = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice.Id);
        Assert.NotNull(invoiceBeforePayment);

        var promptBeforePayment = invoiceBeforePayment!.GetPaymentPrompt(paymentMethodId);
        Assert.NotNull(promptBeforePayment);

        var expectedDue = promptBeforePayment!.Calculate().Due;

        var controller = tester.PayTester.GetController<UIPayJoinController>();
        var bip21Result = Assert.IsType<OkObjectResult>(await controller.GetBip21(invoice.Id));
        var bip21Response = Assert.IsType<GetBip21Response>(bip21Result.Value);
        Assert.True(bip21Response.PayjoinEnabled);

        var paymentUrl = new Uri(bip21Response.Bip21, UriKind.Absolute);
        var paymentActionResult = await controller.RunTestPayment(new RunTestPaymentRequest
        {
            InvoiceId = invoice.Id,
            PaymentUrl = paymentUrl
        }, default);
        var paymentResult = Assert.IsType<OkObjectResult>(paymentActionResult.Result);
        var paymentResponse = Assert.IsType<RunTestPaymentResponse>(paymentResult.Value);
        Assert.True(paymentResponse.Succeeded, paymentResponse.Message);
        Assert.False(string.IsNullOrWhiteSpace(paymentResponse.Message));

        await user.WaitInvoicePaid(invoice.Id);

        var invoiceEntity = await tester.PayTester.GetService<InvoiceRepository>().GetInvoice(invoice.Id);
        Assert.NotNull(invoiceEntity);
        var totalPaid = invoiceEntity
            !
            .GetPayments(false)
            .Where(p => p.Accounted && p.PaymentMethodId == paymentMethodId)
            .Sum(p => p.Value);

        Assert.Equal(expectedDue, totalPaid);
    }
}
