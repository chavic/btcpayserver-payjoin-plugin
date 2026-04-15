using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinBitcoinCheckoutModelExtension : ICheckoutModelExtension
{
    private const string CryptoCode = "BTC";
    private readonly BitcoinCheckoutModelExtension _innerExtension;
    private readonly PayjoinInvoicePaymentUrlService _paymentUrlService;

    public PayjoinBitcoinCheckoutModelExtension(
        BTCPayNetworkProvider networkProvider,
        IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
        DisplayFormatter displayFormatter,
        PayjoinInvoicePaymentUrlService paymentUrlService)
    {
        ArgumentNullException.ThrowIfNull(networkProvider);
        ArgumentNullException.ThrowIfNull(paymentLinkExtensions);
        ArgumentNullException.ThrowIfNull(displayFormatter);
        ArgumentNullException.ThrowIfNull(paymentUrlService);

        PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(CryptoCode);
        var network = networkProvider.GetNetwork<BTCPayNetwork>(CryptoCode)
            ?? throw new InvalidOperationException($"Network not available for {CryptoCode}");
        _innerExtension = new BitcoinCheckoutModelExtension(PaymentMethodId, network, paymentLinkExtensions, displayFormatter);
        _paymentUrlService = paymentUrlService;
    }

    public PaymentMethodId PaymentMethodId { get; }
    public string Image => _innerExtension.Image;
    public string Badge => _innerExtension.Badge;

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _innerExtension.ModifyCheckoutModel(context);

        if (context.InvoiceEntity.GetInvoiceState().Status != InvoiceStatus.New)
        {
            return;
        }

        var paymentUrl = _paymentUrlService.GetCheckoutPaymentUrl(context);
        if (paymentUrl is null)
        {
            return;
        }

        context.Model.InvoiceBitcoinUrl = MergePayjoinIntoPaymentUrl(context.Model.InvoiceBitcoinUrl, paymentUrl.Bip21);
        context.Model.InvoiceBitcoinUrlQR = MergePayjoinIntoPaymentUrl(context.Model.InvoiceBitcoinUrlQR, paymentUrl.Bip21);
    }

    internal static string MergePayjoinIntoPaymentUrl(string baseUrl, string payjoinUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return payjoinUrl;
        }

        if (string.IsNullOrWhiteSpace(payjoinUrl))
        {
            return baseUrl;
        }

        var payjoinParameter = ExtractQueryParameter(payjoinUrl, PayjoinClient.BIP21EndpointKey);
        return UpsertQueryParameter(baseUrl, PayjoinClient.BIP21EndpointKey, payjoinParameter);
    }

    internal static string? ExtractQueryParameter(string url, string key)
    {
        var query = GetQuery(url);
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (HasQueryKey(segment, key))
            {
                return segment;
            }
        }

        return null;
    }

    internal static string UpsertQueryParameter(string url, string key, string? rawSegment)
    {
        var querySeparatorIndex = url.IndexOf('?', StringComparison.Ordinal);
        var prefix = querySeparatorIndex >= 0 ? url[..querySeparatorIndex] : url;
        var query = querySeparatorIndex >= 0 ? url[(querySeparatorIndex + 1)..] : string.Empty;

        var segments = new List<string>();
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!HasQueryKey(segment, key))
                {
                    segments.Add(segment);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rawSegment))
        {
            segments.Add(rawSegment);
        }

        return segments.Count == 0
            ? prefix
            : $"{prefix}?{string.Join("&", segments)}";
    }

    private static string? GetQuery(string url)
    {
        var querySeparatorIndex = url.IndexOf('?', StringComparison.Ordinal);
        return querySeparatorIndex >= 0 && querySeparatorIndex < url.Length - 1
            ? url[(querySeparatorIndex + 1)..]
            : null;
    }

    private static bool HasQueryKey(string segment, string key)
    {
        var keyValueSeparatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
        var segmentKey = keyValueSeparatorIndex >= 0 ? segment[..keyValueSeparatorIndex] : segment;
        return string.Equals(segmentKey, key, StringComparison.OrdinalIgnoreCase);
    }
}
