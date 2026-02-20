using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Payjoin.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Payjoin;

public class Plugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    };

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("header-nav", "PayjoinHeaderNav");
        applicationBuilder.AddUIExtension("store-nav", "PayjoinStoreNavExtension");
        applicationBuilder.AddUIExtension("checkout-end", "PayJoinCheckoutExtension");
        applicationBuilder.AddHostedService<ApplicationPartsLogger>();
        applicationBuilder.AddSingleton<PayjoinDemoContext>();
        applicationBuilder.AddSingleton<PayjoinDemoInitializer>();
        applicationBuilder.AddSingleton<PayjoinReceiverSessionStore>();
        applicationBuilder.AddSingleton<PayjoinOhttpKeysProvider>();
        applicationBuilder.AddSingleton<PayjoinBip21Service>();
        applicationBuilder.AddHostedService<PayjoinReceiverPoller>();
        applicationBuilder.AddHostedService(provider => provider.GetRequiredService<PayjoinDemoInitializer>());
        applicationBuilder.AddHostedService<PluginMigrationRunner>();
        applicationBuilder.AddSingleton<PayjoinPluginService>();
        applicationBuilder.AddSingleton<IPayjoinStoreSettingsRepository, PayjoinStoreSettingsRepository>();
        applicationBuilder.AddSingleton<PayjoinPluginDbContextFactory>();
        applicationBuilder.AddDbContext<PayjoinPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PayjoinPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
    }
}
