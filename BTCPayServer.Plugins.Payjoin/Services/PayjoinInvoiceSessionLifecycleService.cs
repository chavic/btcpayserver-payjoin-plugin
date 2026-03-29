using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinInvoiceSessionLifecycleService : EventHostedServiceBase
{
    private static readonly Action<ILogger, string, InvoiceStatus, Exception?> LogReceiverSessionCloseRequestedForInvoiceState =
        LoggerMessage.Define<string, InvoiceStatus>(
            LogLevel.Information,
            new EventId(1, nameof(LogReceiverSessionCloseRequestedForInvoiceState)),
            "Payjoin receiver session close requested for {InvoiceId} after invoice state changed to {InvoiceStatus}");

    private readonly PayjoinReceiverSessionStore _sessionStore;
    private readonly ILogger<PayjoinInvoiceSessionLifecycleService> _logger;

    public PayjoinInvoiceSessionLifecycleService(
        EventAggregator eventAggregator,
        PayjoinReceiverSessionStore sessionStore,
        ILogger<PayjoinInvoiceSessionLifecycleService> logger)
        : base(eventAggregator, logger)
    {
        _sessionStore = sessionStore;
        _logger = logger;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceDataChangedEvent>();
    }

    protected override Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (evt is not InvoiceDataChangedEvent invoiceDataChangedEvent || !ShouldRequestClose(invoiceDataChangedEvent.State.Status))
        {
            return Task.CompletedTask;
        }

        TryRequestClose(invoiceDataChangedEvent);

        return Task.CompletedTask;
    }

    private static bool ShouldRequestClose(InvoiceStatus status)
    {
        return status != InvoiceStatus.New;
    }

    private void TryRequestClose(InvoiceDataChangedEvent invoiceDataChangedEvent)
    {
        if (!_sessionStore.RequestClose(invoiceDataChangedEvent.InvoiceId, invoiceDataChangedEvent.State.Status))
        {
            return;
        }

        LogReceiverSessionCloseRequestedForInvoiceState(_logger, invoiceDataChangedEvent.InvoiceId, invoiceDataChangedEvent.State.Status, null);
    }
}
