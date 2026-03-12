using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Payjoin;
using Payjoin.Http;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SystemUri = System.Uri;

namespace BTCPayServer.Plugins.Payjoin.Services;

public sealed class PayjoinOhttpKeysProvider
{
    // TODO: Consider making this configurable if 12 hours is not a good duration for caching OHTTP keys.
    private static readonly TimeSpan OhttpKeysCacheDuration = TimeSpan.FromHours(12);

    private static readonly Action<ILogger, string, string, Exception?> LogFetchFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(GetKeysAsync)),
            "Failed to fetch OHTTP keys from {OhttpRelayUrl} for store {StoreId}");

    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<PayjoinOhttpKeysProvider> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new();

    public PayjoinOhttpKeysProvider(IMemoryCache memoryCache, ILogger<PayjoinOhttpKeysProvider> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
    }

    internal async Task<OhttpKeys?> GetKeysAsync(
        SystemUri ohttpRelayUrl,
        string directoryUrl,
        string storeId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"PayjoinOhttpKeys_{storeId}_{ohttpRelayUrl}";

        if (_memoryCache.TryGetValue(cacheKey, out OhttpKeys? ohttpKeys))
        {
            return ohttpKeys;
        }

        var semaphore = _fetchLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out ohttpKeys))
            {
                return ohttpKeys;
            }

            //var ohttpKeysClient = new OhttpKeysClient(_httpClientFactory.CreateClient(nameof(PayjoinOhttpKeysProvider)));
            using var ohttpKeysClient = new OhttpKeysClient(ohttpRelayUrl);
            // TODO: Consider adding some retry logic and refreshing the keys on failure.
            ohttpKeys = await ohttpKeysClient.GetOhttpKeysAsync(new SystemUri(directoryUrl), cancellationToken).ConfigureAwait(false);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(OhttpKeysCacheDuration)
                .RegisterPostEvictionCallback(static (_, value, _, _) => (value as IDisposable)?.Dispose());
            _memoryCache.Set(cacheKey, ohttpKeys, cacheOptions);
            return ohttpKeys;
        }
        catch (Exception e) when (e is UniffiException or HttpRequestException)
        {
            LogFetchFailure(_logger, ohttpRelayUrl.AbsoluteUri, storeId, e);
            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
