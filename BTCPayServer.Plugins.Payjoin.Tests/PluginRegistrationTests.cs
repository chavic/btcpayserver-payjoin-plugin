using BTCPayServer.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class PluginRegistrationTests
{
    [Fact]
    public void PluginRegistersBitcoinCheckoutExtensions()
    {
        var services = new ServiceCollection();

        new Plugin().Execute(services);

        var uiExtensions = services
            .Where(descriptor => descriptor.ServiceType == typeof(IUIExtension))
            .Select(descriptor => Assert.IsAssignableFrom<IUIExtension>(descriptor.ImplementationInstance))
            .ToArray();

        Assert.Contains(uiExtensions, extension =>
            extension.Location == "checkout-bitcoin-post-content" &&
            extension.Partial == "PayJoinBitcoinCheckoutPostContent");
        Assert.Contains(uiExtensions, extension =>
            extension.Location == "checkout-end" &&
            extension.Partial == "PayJoinBitcoinCheckoutEnd");
    }
}
