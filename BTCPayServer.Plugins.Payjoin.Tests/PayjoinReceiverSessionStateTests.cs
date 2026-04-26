using BTCPayServer.Plugins.Payjoin.Services;
using NBitcoin;
using System;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class PayjoinReceiverSessionStateTests
{
    [Fact]
    public void ContributedInputIsMissingByDefault()
    {
        var session = CreateSession();

        Assert.False(session.TryGetContributedInput(out _));
    }

    [Fact]
    public void ContributedInputRestoresFromPersistedMetadata()
    {
        var contributedOutPoint = new OutPoint(uint256.Parse("1111111111111111111111111111111111111111111111111111111111111111"), 1);
        var session = CreateSession(
            contributedInputTransactionId: contributedOutPoint.Hash.ToString(),
            contributedInputOutputIndex: checked((int)contributedOutPoint.N));

        Assert.True(session.TryGetContributedInput(out var restoredOutPoint));
        Assert.Equal(contributedOutPoint, restoredOutPoint);
    }

    [Fact]
    public void EventsAreReturnedAsCopy()
    {
        var session = CreateSession(events: new[] { "event-1", "event-2" });

        var events = session.GetEvents();
        events[0] = "mutated";

        Assert.Equal(new[] { "event-1", "event-2" }, session.GetEvents());
    }

    [Fact]
    public void InvalidPersistedContributedInputMetadataIsIgnored()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new PayjoinReceiverSessionState(
            "invoice-1",
            "store-1",
            "bcrt1qexampleaddress0000000000000000000000000",
            new Uri("https://relay.example/"),
            now.AddMinutes(5),
            now,
            now,
            contributedInputTransactionId: "not-a-txid",
            contributedInputOutputIndex: 1);

        Assert.False(session.TryGetContributedInput(out _));
    }

    private static PayjoinReceiverSessionState CreateSession(
        DateTimeOffset? updatedAt = null,
        string? contributedInputTransactionId = null,
        int? contributedInputOutputIndex = null,
        string[]? events = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PayjoinReceiverSessionState(
            "invoice-1",
            "store-1",
            "bcrt1qexampleaddress0000000000000000000000000",
            new Uri("https://relay.example/"),
            now.AddMinutes(5),
            now,
            updatedAt ?? now,
            contributedInputTransactionId: contributedInputTransactionId,
            contributedInputOutputIndex: contributedInputOutputIndex,
            events: events);
    }
}
