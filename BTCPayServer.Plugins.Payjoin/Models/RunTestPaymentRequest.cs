using System;

namespace BTCPayServer.Plugins.Payjoin.Models;

public sealed class RunTestPaymentRequest
{
    public string? InvoiceId { get; set; }
    public Uri? PaymentUrl { get; set; }
}
