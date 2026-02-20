using BTCPayServer.Plugins.Payjoin.Controllers;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
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
    public void GetBip21ReturnsExpectedPayload()
    {
        using var controller = new UIPayJoinController(null!);

        var result = controller.GetBip21("invoice-1");

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.NotNull(jsonResult.Value);

        var valueType = jsonResult.Value!.GetType();
        var bip21 = (string?)valueType.GetProperty("bip21")?.GetValue(jsonResult.Value);
        var payjoinEnabled = (bool?)valueType.GetProperty("payjoinEnabled")?.GetValue(jsonResult.Value);

        Assert.False(string.IsNullOrEmpty(bip21));
        Assert.StartsWith("bitcoin:", bip21, StringComparison.OrdinalIgnoreCase);
        Assert.True(payjoinEnabled);
    }

    [Fact]
    public void RunTestPaymentReturnsNotFoundWhenCheatModeDisabled()
    {
        using var controller = new UIPayJoinController(CreateEnvironment(false));

        var result = controller.RunTestPayment(new RunTestPaymentRequest
        {
            InvoiceId = "invoice-1"
        });

        Assert.IsType<NotFoundResult>(result);
    }
}
