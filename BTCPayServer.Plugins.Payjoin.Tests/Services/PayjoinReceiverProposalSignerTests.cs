using BTCPayServer.Plugins.Payjoin.Services;
using BTCPayServer.Services.Wallets;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests.Services;

public class PayjoinReceiverProposalSignerTests
{
    [Fact]
    public void EnsureContributedInputsPresentSucceedsWhenAllContributedInputsExist()
    {
        // Arrange
        var outPoint = new OutPoint(uint256.Parse("1111111111111111111111111111111111111111111111111111111111111111"), 0);
        var psbt = CreatePsbtWithInputs(outPoint);
        var receivedCoins = new[] { CreateReceivedCoin(outPoint, Money.Satoshis(50_000), CreateScript(1)) };

        // Act
        var exception = Record.Exception(() => PayjoinReceiverProposalSigner.EnsureContributedInputsPresent(psbt, receivedCoins));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureContributedInputsPresentThrowsWhenInputMissing()
    {
        // Arrange
        var missingOutPoint = new OutPoint(uint256.Parse("2222222222222222222222222222222222222222222222222222222222222222"), 1);
        var psbt = CreatePsbtWithInputs(new OutPoint(uint256.Parse("3333333333333333333333333333333333333333333333333333333333333333"), 0));
        var receivedCoins = new[] { CreateReceivedCoin(missingOutPoint, Money.Satoshis(10_000), CreateScript(2)) };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => PayjoinReceiverProposalSigner.EnsureContributedInputsPresent(psbt, receivedCoins));

        // Assert
        Assert.Contains(missingOutPoint.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureContributedInputsPresentThrowsWhenMultipleInputsMissing()
    {
        // Arrange
        var missingOutPoint1 = new OutPoint(uint256.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), 0);
        var missingOutPoint2 = new OutPoint(uint256.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), 1);
        var psbt = CreatePsbtWithInputs(new OutPoint(uint256.Parse("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"), 2));
        var receivedCoins =
        new[]
        {
            CreateReceivedCoin(missingOutPoint1, Money.Satoshis(10_000), CreateScript(2)),
            CreateReceivedCoin(missingOutPoint2, Money.Satoshis(20_000), CreateScript(3))
        };

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => PayjoinReceiverProposalSigner.EnsureContributedInputsPresent(psbt, receivedCoins));

        // Assert
        Assert.Contains(missingOutPoint1.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(missingOutPoint2.ToString(), exception.Message, StringComparison.Ordinal);
    }

    private static PSBT CreatePsbtWithInputs(params OutPoint[] outPoints)
    {
        var network = Network.RegTest;
        var transaction = network.CreateTransaction();
        foreach (var outPoint in outPoints)
        {
            transaction.Inputs.Add(new TxIn(outPoint));
        }

        transaction.Outputs.Add(Money.Satoshis(1000), CreateScript(9));
        return PSBT.FromTransaction(transaction, network);
    }

    private static ReceivedCoin CreateReceivedCoin(OutPoint outPoint, Money amount, Script scriptPubKey)
    {
        return new ReceivedCoin
        {
            OutPoint = outPoint,
            ScriptPubKey = scriptPubKey,
            Value = amount,
            Coin = new Coin(outPoint, new TxOut(amount, scriptPubKey)),
            Timestamp = DateTimeOffset.UtcNow,
            Confirmations = 1
        };
    }

    private static Script CreateScript(int seed)
    {
        var keyBytes = Enumerable.Repeat((byte)seed, 32).ToArray();
        using var key = new Key(keyBytes);
        return key.PubKey.WitHash.ScriptPubKey;
    }
}
