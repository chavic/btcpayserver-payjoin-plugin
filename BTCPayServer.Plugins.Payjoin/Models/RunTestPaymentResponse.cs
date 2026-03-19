namespace BTCPayServer.Plugins.Payjoin.Models;

public sealed record RunTestPaymentResponse(bool Succeeded, string Message, string? TransactionId = null)
{
    public static RunTestPaymentResponse Failure(string message)
    {
        return new(false, message);
    }

    public static RunTestPaymentResponse Success(string message, string transactionId)
    {
        return new(true, message, transactionId);
    }
}
