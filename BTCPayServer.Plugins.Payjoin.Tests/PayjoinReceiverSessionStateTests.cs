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
    public void ContributedInputRoundTripsThroughPersistedMetadata()
    {
        var session = CreateSession();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var contributedOutPoint = new OutPoint(uint256.Parse("1111111111111111111111111111111111111111111111111111111111111111"), 1);

        session.SetContributedInput(contributedOutPoint, updatedAt);

        Assert.True(session.TryGetContributedInput(out var restoredOutPoint));
        Assert.Equal(contributedOutPoint, restoredOutPoint);
        Assert.Equal(updatedAt, session.UpdatedAt);

        var snapshot = session.Snapshot();
        Assert.Equal(contributedOutPoint.Hash.ToString(), snapshot.ContributedInputTransactionId);
        Assert.Equal((int)contributedOutPoint.N, snapshot.ContributedInputOutputIndex);
    }

    [Fact]
    public void SettingTheSameContributedInputDoesNotAdvanceUpdatedAt()
    {
        var initialUpdatedAt = DateTimeOffset.UtcNow;
        var session = CreateSession(updatedAt: initialUpdatedAt);
        var contributedOutPoint = new OutPoint(uint256.Parse("2222222222222222222222222222222222222222222222222222222222222222"), 2);
        var firstPersistedAt = initialUpdatedAt.AddMinutes(1);

        session.SetContributedInput(contributedOutPoint, firstPersistedAt);
        session.SetContributedInput(contributedOutPoint, initialUpdatedAt.AddMinutes(2));

        Assert.Equal(firstPersistedAt, session.UpdatedAt);
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

    private static PayjoinReceiverSessionState CreateSession(DateTimeOffset? updatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PayjoinReceiverSessionState(
            "invoice-1",
            "store-1",
            "bcrt1qexampleaddress0000000000000000000000000",
            new Uri("https://relay.example/"),
            now.AddMinutes(5),
            now,
            updatedAt ?? now);
    }
}
