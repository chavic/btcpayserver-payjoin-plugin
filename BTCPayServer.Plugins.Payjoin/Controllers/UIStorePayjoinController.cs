using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Plugins.Payjoin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Controllers;

[Route("~/stores/{storeId}/payjoin")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewStoreSettings)]
public class UIStorePayjoinController : Controller
{
    private readonly IPayjoinStoreSettingsRepository _settingsRepository;

    public UIStorePayjoinController(IPayjoinStoreSettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    [HttpGet("")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var store = HttpContext.GetStoreData();
        if (store is null)
        {
            return NotFound();
        }

        var settings = await _settingsRepository.GetAsync(storeId).ConfigureAwait(false);
        var vm = new PayjoinStoreSettingsViewModel
        {
            StoreId = storeId,
            EnabledByDefault = settings.EnabledByDefault,
            DirectoryUrl = settings.DirectoryUrl,
            OhttpRelayUrl = settings.OhttpRelayUrl,
            LayoutModel = new LayoutModel("Payjoin", "Payjoin").SetCategory(WellKnownCategories.Store)
        };
        ViewData.SetLayoutModel(vm.LayoutModel);
        return View(vm);
    }

    [HttpPost("")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> SettingsPost(string storeId, PayjoinStoreSettingsViewModel model)
    {
        if (model is null)
        {
            return BadRequest();
        }
        var store = HttpContext.GetStoreData();
        if (store is null)
        {
            return NotFound();
        }

        model.StoreId = storeId;
        model.LayoutModel = new LayoutModel("Payjoin", "Payjoin").SetCategory(WellKnownCategories.Store);
        if (!ModelState.IsValid)
        {
            ViewData.SetLayoutModel(model.LayoutModel);
            return View("Settings", model);
        }

        var settings = new PayjoinStoreSettings
        {
            EnabledByDefault = model.EnabledByDefault,
            DirectoryUrl = model.DirectoryUrl,
            OhttpRelayUrl = model.OhttpRelayUrl
        };

        await _settingsRepository.SetAsync(storeId, settings).ConfigureAwait(false);
        TempData[WellKnownTempData.SuccessMessage] = "Payjoin settings saved.";
        return RedirectToAction(nameof(Settings), new { storeId });
    }
}
