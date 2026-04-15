using BTCPayServer.Plugins.Payjoin.Services;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class PayjoinBitcoinCheckoutModelExtensionTests
{
    [Fact]
    public void MergePayjoinIntoPaymentUrlPreservesExistingQueryParameters()
    {
        const string baseUrl = "bitcoin:bcrt1qexample?amount=0.10000000&lightning=lnbcrt123";
        const string payjoinUrl = "bitcoin:bcrt1qexample?amount=0.10000000&pj=https%3A%2F%2Fexample.com%2Fpj";

        var merged = PayjoinBitcoinCheckoutModelExtension.MergePayjoinIntoPaymentUrl(baseUrl, payjoinUrl);

        Assert.Equal(
            "bitcoin:bcrt1qexample?amount=0.10000000&lightning=lnbcrt123&pj=https%3A%2F%2Fexample.com%2Fpj",
            merged);
    }

    [Fact]
    public void MergePayjoinIntoPaymentUrlRemovesStalePayjoinParameterWhenFallingBackToPlainBip21()
    {
        const string baseUrl = "bitcoin:BCRT1QEXAMPLE?amount=0.10000000&lightning=LNBCRT123&pj=https%3A%2F%2Fold.example%2Fpj";
        const string payjoinFallbackUrl = "bitcoin:bcrt1qexample?amount=0.10000000";

        var merged = PayjoinBitcoinCheckoutModelExtension.MergePayjoinIntoPaymentUrl(baseUrl, payjoinFallbackUrl);

        Assert.Equal("bitcoin:BCRT1QEXAMPLE?amount=0.10000000&lightning=LNBCRT123", merged);
    }
}
