using BTCPayServer.Plugins.Payjoin.Controllers;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class UIPayJoinControllerTests
{
    private static BTCPayServerEnvironment CreateEnvironment(bool cheatMode)
    {
        var env = (BTCPayServerEnvironment)RuntimeHelpers.GetUninitializedObject(typeof(BTCPayServerEnvironment));
        typeof(BTCPayServerEnvironment).GetProperty("CheatMode")?.SetValue(env, cheatMode);
        return env;
    }

    [Fact]
    public async Task GetBip21ReturnsBadRequestWhenInvoiceIdMissing()
    {
        using var controller = new UIPayJoinController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger<UIPayJoinController>.Instance);

        var result = await controller.GetBip21(" ", "standard", TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RunTestPaymentReturnsNotFoundWhenCheatModeDisabled()
    {
        using var controller = new UIPayJoinController(CreateEnvironment(false), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger<UIPayJoinController>.Instance);

        var result = await controller.RunTestPayment(new RunTestPaymentRequest
        {
            InvoiceId = "invoice-1"
        }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }
}
