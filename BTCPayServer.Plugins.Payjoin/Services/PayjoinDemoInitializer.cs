using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Payjoin.Models;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinDemoInitializer : IHostedService
{
    private readonly PayjoinDemoContext _context;
    private readonly IPayjoinStoreSettingsRepository _settingsRepository;

    public PayjoinDemoInitializer(
        PayjoinDemoContext context,
        IPayjoinStoreSettingsRepository settingsRepository)
    {
        _context = context;
        _settingsRepository = settingsRepository;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var demoStores = new List<(string StoreId, PayjoinStoreSettings Settings)>();
        var stores = await _settingsRepository.GetAllAsync().ConfigureAwait(false);
        foreach (var store in stores)
        {
            var settings = store.Settings;
            if (!settings.DemoMode)
            {
                continue;
            }

            demoStores.Add((store.StoreId, settings));
        }

        if (demoStores.Count == 0)
        {
            return;
        }

        foreach (var demoStore in demoStores)
        {
            await InitializeDemoSettingsAsync(demoStore.StoreId, demoStore.Settings, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<PayjoinStoreSettings> InitializeDemoSettingsAsync(
        string storeId,
        PayjoinStoreSettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryApplyDemoSettings(settings))
        {
            return settings;
        }

        await _settingsRepository.SetAsync(storeId, settings).ConfigureAwait(false);
        return settings;
    }

    public bool TryApplyDemoSettings(PayjoinStoreSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.DemoMode)
        {
            return false;
        }

        if (!_context.IsReady)
        {
            try
            {
                _context.Initialize();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return false;
            }
        }

        if (_context.DirectoryUrl is not null)
        {
            settings.DirectoryUrl = _context.DirectoryUrl;
        }

        if (_context.OhttpRelayUrl is not null)
        {
            settings.OhttpRelayUrl = _context.OhttpRelayUrl;
        }

        return true;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
