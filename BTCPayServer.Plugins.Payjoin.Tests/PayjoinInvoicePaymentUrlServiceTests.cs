using BTCPayServer.Plugins.Payjoin.Services;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class PayjoinInvoicePaymentUrlServiceTests
{
    [Fact]
    public async Task GetInvoicePaymentUrlAsyncThrowsWhenInvoiceIdMissing()
    {
        var service = new PayjoinInvoicePaymentUrlService(null!, null!, null!, null!);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetInvoicePaymentUrlAsync(" ", TestContext.Current.CancellationToken));
    }
}
