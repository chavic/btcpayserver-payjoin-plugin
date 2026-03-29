using BTCPayServer.Plugins.Payjoin.Controllers;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class UIPayJoinControllerTests
{
    private static UIPayJoinController CreateController(bool cheatMode)
    {
        return new UIPayJoinController(CreateEnvironment(cheatMode), null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static RunTestPaymentResponse AssertRunTestPaymentFailure(ActionResult<RunTestPaymentResponse> actionResult, string expectedMessage)
    {
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<RunTestPaymentResponse>(okResult.Value);
        Assert.False(response.Succeeded);
        Assert.Equal(expectedMessage, response.Message);
        Assert.Null(response.TransactionId);
        return response;
    }

    private static BTCPayServerEnvironment CreateEnvironment(bool cheatMode)
    {
        var env = (BTCPayServerEnvironment)RuntimeHelpers.GetUninitializedObject(typeof(BTCPayServerEnvironment));
        typeof(BTCPayServerEnvironment).GetProperty("CheatMode")?.SetValue(env, cheatMode);
        return env;
    }

    [Fact]
    public async Task RunTestPaymentReturnsNotFoundWhenCheatModeDisabled()
    {
        using var controller = CreateController(false);

        var result = await controller.RunTestPayment(new RunTestPaymentRequest
        {
            InvoiceId = "invoice-1"
        }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task RunTestPaymentThrowsWhenRequestIsNull()
    {
        using var controller = CreateController(true);

        await Assert.ThrowsAsync<ArgumentNullException>(() => controller.RunTestPayment(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunTestPaymentReturnsFailureWhenInvoiceIdMissing()
    {
        using var controller = CreateController(true);

        var result = await controller.RunTestPayment(new RunTestPaymentRequest(), TestContext.Current.CancellationToken);

        AssertRunTestPaymentFailure(result, "invoiceId is required");
    }

    [Fact]
    public async Task RunTestPaymentReturnsFailureWhenPaymentUrlMissing()
    {
        using var controller = CreateController(true);

        var result = await controller.RunTestPayment(new RunTestPaymentRequest
        {
            InvoiceId = "invoice-1"
        }, TestContext.Current.CancellationToken);

        AssertRunTestPaymentFailure(result, "paymentUrl is required");
    }
}
