using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.Payjoin.Services;
using Xunit;
using SystemUri = System.Uri;

namespace BTCPayServer.Plugins.Payjoin.Tests.Services;

public class PayjoinReceiverStateProcessorTests
{
    [Fact]
    public void OwnershipResolverTreatsInvoiceReceiverScriptAsOwned()
    {
        // The invoice's own receiver script is always owned without needing a wallet lookup, so the
        // explorer client and derivation are never touched for this fast path.
        var receiverScript = new byte[] { 0x01, 0x02, 0x03 };
        var resolver = new PayjoinScriptOwnershipResolver(client: null!, accountDerivation: null!, receiverScript);

        Assert.True(resolver.IsOwned(new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Fact]
    public void CloseRequestedBroadcastGuardReflectsCloseRequestedState()
    {
        var openGuard = new PayjoinReceiverStateProcessor.CloseRequestedBroadcastGuard(CreateSession(isCloseRequested: false));
        var closedGuard = new PayjoinReceiverStateProcessor.CloseRequestedBroadcastGuard(CreateSession(isCloseRequested: true));

        var open = openGuard.Callback(Array.Empty<byte>());
        var closed = closedGuard.Callback(Array.Empty<byte>());

        Assert.True(open);
        Assert.False(closed);
    }

    private static PayjoinReceiverSessionState CreateSession(
        string? invoiceId = null,
        string? storeId = null,
        string? receiverAddress = null,
        SystemUri? ohttpRelayUrl = null,
        DateTimeOffset? monitoringExpiresAt = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        bool isCloseRequested = false,
        InvoiceStatus? closeInvoiceStatus = null,
        DateTimeOffset? closeRequestedAt = null,
        bool initializedPollAfterCloseRequestConsumed = false,
        string? contributedInputTransactionId = null,
        long? contributedInputOutputIndex = null,
        IEnumerable<string>? events = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PayjoinReceiverSessionState(
            invoiceId ?? "invoice-1",
            storeId ?? "store-1",
            receiverAddress ?? "bcrt1qexampleaddress0000000000000000000000000",
            ohttpRelayUrl ?? new SystemUri("https://relay.example/"),
            monitoringExpiresAt ?? now.AddHours(1),
            createdAt ?? now,
            updatedAt ?? now,
            isCloseRequested,
            closeInvoiceStatus,
            closeRequestedAt,
            initializedPollAfterCloseRequestConsumed,
            contributedInputTransactionId,
            contributedInputOutputIndex,
            events);
    }
}
