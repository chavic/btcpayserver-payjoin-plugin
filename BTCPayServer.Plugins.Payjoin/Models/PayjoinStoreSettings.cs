using System;

namespace BTCPayServer.Plugins.Payjoin.Models;

public class PayjoinStoreSettings
{
    public static Uri DefaultDirectoryUrl { get; } = new("https://payjo.in/");

    public static Uri DefaultOhttpRelayUrl { get; } = new("https://pj.bobspacebkk.com");

    public bool EnabledByDefault { get; set; }

    public Uri? DirectoryUrl { get; set; } = DefaultDirectoryUrl;

    public Uri? OhttpRelayUrl { get; set; } = DefaultOhttpRelayUrl;
}
