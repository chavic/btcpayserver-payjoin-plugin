using BTCPayServer.Payments;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Services.Invoices;
using Payjoin;
using System;
using System.Threading;
using System.Threading.Tasks;
using PayjoinUri = Payjoin.Uri;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinInvoicePaymentUrlService
{
    private const string CryptoCode = "BTC";

    private readonly InvoiceRepository _invoiceRepository;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IPayjoinStoreSettingsRepository _storeSettingsRepository;
    private readonly PayjoinUriSessionService _payjoinUriSessionService;

    public PayjoinInvoicePaymentUrlService(
        InvoiceRepository invoiceRepository,
        BTCPayNetworkProvider networkProvider,
        IPayjoinStoreSettingsRepository storeSettingsRepository,
        PayjoinUriSessionService payjoinUriSessionService)
    {
        _invoiceRepository = invoiceRepository;
        _networkProvider = networkProvider;
        _storeSettingsRepository = storeSettingsRepository;
        _payjoinUriSessionService = payjoinUriSessionService;
    }

    public async Task<GetBip21Response?> GetInvoicePaymentUrlAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(invoiceId));
        }

        var invoice = await _invoiceRepository.GetInvoice(invoiceId).ConfigureAwait(false);
        if (invoice is null)
        {
            return null;
        }

        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(CryptoCode);
        var prompt = invoice.GetPaymentPrompt(paymentMethodId);
        if (prompt?.Destination is null)
        {
            return null;
        }

        var network = _networkProvider.GetNetwork<BTCPayNetwork>(CryptoCode);
        if (network is null)
        {
            return null;
        }

        var storeSettings = await _storeSettingsRepository.GetAsync(invoice.StoreId).ConfigureAwait(false);
        var calculation = prompt.Calculate();
        var paymentUrl = await _payjoinUriSessionService.BuildAsync(
            CryptoCode,
            prompt.Destination,
            calculation.Due,
            storeSettings,
            storeSettings.EnabledByDefault,
            invoice.Id,
            invoice.StoreId,
            invoice.MonitoringExpiration,
            cancellationToken).ConfigureAwait(false);

        return new GetBip21Response
        {
            Bip21 = paymentUrl,
            PayjoinEnabled = IsPayjoinEnabled(paymentUrl)
        };
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
}
