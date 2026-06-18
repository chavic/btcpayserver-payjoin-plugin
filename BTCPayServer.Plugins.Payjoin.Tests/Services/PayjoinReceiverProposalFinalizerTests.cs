using BTCPayServer.Plugins.Payjoin.Services;
using BTCPayServer.Services.Wallets;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests.Services;

public class PayjoinReceiverProposalFinalizerTests
{
    [Fact]
    public void ContributedInputSignerSignsOnlyContributedInputs()
    {
        // Arrange: a proposal PSBT with one receiver-contributed input and one sender input.
        var network = Network.RegTest;
        var accountKey = new ExtKey();
        var keyPath = new KeyPath("0/0");
        var contributedKey = accountKey.Derive(keyPath).PrivateKey;
        var contributedScript = contributedKey.PubKey.WitHash.ScriptPubKey;

        var contributedOutpoint = new OutPoint(uint256.Parse("1111111111111111111111111111111111111111111111111111111111111111"), 0);
        var senderOutpoint = new OutPoint(uint256.Parse("2222222222222222222222222222222222222222222222222222222222222222"), 1);

        using var sinkKey = new Key();
        var tx = network.CreateTransaction();
        tx.Inputs.Add(contributedOutpoint);
        tx.Inputs.Add(senderOutpoint);
        tx.Outputs.Add(Money.Coins(1.9m), sinkKey.PubKey.WitHash.ScriptPubKey);
        var psbt = PSBT.FromTransaction(tx, network);

        var receivedCoin = new ReceivedCoin
        {
            OutPoint = contributedOutpoint,
            ScriptPubKey = contributedScript,
            KeyPath = keyPath,
            Coin = new Coin(contributedOutpoint, new TxOut(Money.Coins(1m), contributedScript))
        };

        var signer = new PayjoinReceiverProposalSigner.ContributedInputSigner(network, accountKey, new[] { receivedCoin });

        // Act
        var signed = PSBT.Parse(signer.Callback(psbt.ToBase64()), network);

        // Assert: the receiver's contributed input is signed and finalized; the sender input is left untouched.
        var contributed = Assert.Single(signed.Inputs, i => i.PrevOut == contributedOutpoint);
        var sender = Assert.Single(signed.Inputs, i => i.PrevOut == senderOutpoint);
        Assert.NotNull(contributed.FinalScriptWitness);
        Assert.Null(sender.FinalScriptWitness);
        Assert.Null(sender.FinalScriptSig);
    }
}
