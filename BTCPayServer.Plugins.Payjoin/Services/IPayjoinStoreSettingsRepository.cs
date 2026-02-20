using BTCPayServer.Plugins.Payjoin.Models;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.Payjoin.Services;

public interface IPayjoinStoreSettingsRepository
{
    Task<PayjoinStoreSettings> GetAsync(string storeId);
    Task SetAsync(string storeId, PayjoinStoreSettings settings);
}
