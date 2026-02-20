using BTCPayServer.Data;
using BTCPayServer.Plugins.Payjoin.Models;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinStoreSettingsRepository : IPayjoinStoreSettingsRepository
{
    private const string Key = "payjoin.settings";

    private readonly StoreRepository _storeRepository;

    public PayjoinStoreSettingsRepository(StoreRepository storeRepository)
    {
        _storeRepository = storeRepository;
    }

    public async Task<PayjoinStoreSettings> GetAsync(string storeId)
    {
        var store = await _storeRepository.FindStore(storeId).ConfigureAwait(false);
        if (store is null)
        {
            return new PayjoinStoreSettings();
        }

        var blob = store.GetStoreBlob();
        if (blob.AdditionalData is null || !blob.AdditionalData.TryGetValue(Key, out var token) || token is null)
        {
            return new PayjoinStoreSettings();
        }

        try
        {
            return token.ToObject<PayjoinStoreSettings>() ?? new PayjoinStoreSettings();
        }
        catch (JsonException)
        {
            return new PayjoinStoreSettings();
        }
    }

    public async Task SetAsync(string storeId, PayjoinStoreSettings settings)
    {
        var store = await _storeRepository.FindStore(storeId).ConfigureAwait(false);
        if (store is null)
        {
            return;
        }

        var blob = store.GetStoreBlob();
        blob.AdditionalData ??= new Newtonsoft.Json.Linq.JObject();
        blob.AdditionalData[Key] = Newtonsoft.Json.Linq.JToken.FromObject(settings);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store).ConfigureAwait(false);
    }
}
