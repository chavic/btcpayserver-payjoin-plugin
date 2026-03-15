namespace BTCPayServer.Plugins.Payjoin.Models;

public sealed record RunTestPaymentResponse(bool Succeeded, string Message)
{
    public static RunTestPaymentResponse Failure(string message)
    {
        return new(false, message);
    }

    public static RunTestPaymentResponse Success(string message)
    {
        return new(true, message);
    }
}
