using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Payjoin.Controllers;

[Route("~/plugins/payjoin")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class UIPayJoinController : Controller
{
    private readonly BTCPayServerEnvironment _env;

    public UIPayJoinController(BTCPayServerEnvironment env)
    {
        _env = env;
    }

    [AllowAnonymous]
    [HttpGet("invoices/{invoiceId}/bip21")]
    public IActionResult GetBip21(string invoiceId)
    {
        return Json(new
        {
            bip21 = "bitcoin:dummyaddress",
            payjoinEnabled = true
        });
    }

    [HttpPost("run-test-payment")]
    public IActionResult RunTestPayment([FromBody] RunTestPaymentRequest request)
    {
        if (!_env.CheatMode)
        {
            return NotFound();
        }

        return Json(new
        {
            errorMessage = "Payjoin test payment stub"
        });
    }
}
