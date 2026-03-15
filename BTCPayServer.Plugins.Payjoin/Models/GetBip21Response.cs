namespace BTCPayServer.Plugins.Payjoin.Models;

public sealed class GetBip21Response
{
    public required string Bip21 { get; init; }
    public required bool PayjoinEnabled { get; init; }
}
