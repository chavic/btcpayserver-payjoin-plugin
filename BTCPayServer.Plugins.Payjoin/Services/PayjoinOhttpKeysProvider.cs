using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using uniffi.payjoin;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new();

    public PayjoinOhttpKeysProvider(IMemoryCache memoryCache, ILogger<PayjoinOhttpKeysProvider> logger, IHttpClientFactory httpClientFactory)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    internal async Task<OhttpKeys?> GetKeysAsync(
        SystemUri ohttpRelayUrl,
        string directoryUrl,
        string storeId,
        ReadOnlyMemory<byte>? certificate,
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

            // TODO: Consider adding some retry logic and refreshing the keys on failure.
            ohttpKeys = await FetchAndDecodeOhttpKeysViaRelayProxyAsync(ohttpRelayUrl, directoryUrl, certificate?.Span.ToArray(), cancellationToken).ConfigureAwait(false);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(OhttpKeysCacheDuration)
                .RegisterPostEvictionCallback(static (_, value, _, _) => (value as IDisposable)?.Dispose());
            _memoryCache.Set(cacheKey, ohttpKeys, cacheOptions);
            return ohttpKeys;
        }
        catch (UniffiException e)
        {
            LogFetchFailure(_logger, ohttpRelayUrl.AbsoluteUri, storeId, e);
            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<OhttpKeys> FetchAndDecodeOhttpKeysViaRelayProxyAsync(SystemUri ohttpRelay, string directory, byte[]? cert, CancellationToken cancellationToken = default)
    {
        var keysUrl = new SystemUri(new SystemUri(directory), "/.well-known/ohttp-gateway");

        using var handler = new HttpClientHandler
        {
            Proxy = ohttpRelay is null ? null : new System.Net.WebProxy(ohttpRelay),
            UseProxy = ohttpRelay is not null,
            CheckCertificateRevocationList = true
        };

        if (cert is not null && cert is { Length: > 0 })
        {
            handler.ServerCertificateCustomValidationCallback = (_, serverCert, _, _) =>
                serverCert is not null &&
                cert.AsSpan().SequenceEqual(serverCert.GetRawCertData());
        }

        //var client = _httpClientFactory.CreateClient(nameof(PayjoinOhttpKeysProvider));
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, keysUrl);
        request.Headers.Accept.ParseAdd("application/ohttp-keys");

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return OhttpKeys.Decode(body);
    }
}
