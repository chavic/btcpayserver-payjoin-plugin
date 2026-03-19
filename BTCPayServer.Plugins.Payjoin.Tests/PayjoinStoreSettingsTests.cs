using BTCPayServer.Plugins.Payjoin.Models;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class PayjoinStoreSettingsTests
{
    private const string TestXpub = "xpub661MyMwAqRbcFtXgS5sYJABqqG9YLmC4Q1Rdap9gSE8NqtwybGhePY2gZ29ESFjqJoCu1Rupje8YtGqsefD265TMg7usUDFdp6W1EGMcet8";

    [Fact]
    public void NewSettingsHasExpectedDefaults()
    {
        var settings = new PayjoinStoreSettings();

        Assert.False(settings.EnabledByDefault);
        Assert.Equal(PayjoinStoreSettings.DefaultDirectoryUrl, settings.DirectoryUrl);
        Assert.Equal(PayjoinStoreSettings.DefaultOhttpRelayUrl, settings.OhttpRelayUrl);
        Assert.Null(settings.ColdWalletDerivationScheme);
    }

    [Fact]
    public void SettingsPreserveAssignedValues()
    {
        var directoryUrl = new Uri("https://example.com/directory");
        var ohttpRelayUrl = new Uri("https://example.com/relay");

        var settings = new PayjoinStoreSettings
        {
            EnabledByDefault = true,
            DirectoryUrl = directoryUrl,
            OhttpRelayUrl = ohttpRelayUrl,
            ColdWalletDerivationScheme = TestXpub
        };

        Assert.True(settings.EnabledByDefault);
        Assert.Equal(directoryUrl, settings.DirectoryUrl);
        Assert.Equal(ohttpRelayUrl, settings.OhttpRelayUrl);
        Assert.Equal(TestXpub, settings.ColdWalletDerivationScheme);
    }
}
