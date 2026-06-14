using System;
using BTCPayServer.Plugins.Payjoin.Services;
using Xunit;

namespace BTCPayServer.Plugins.Payjoin.Tests;

public class PayjoinUriSessionServiceExpiryTests
{
    [Fact]
    public void ExpirationTracksMonitoringWindow()
    {
        var monitoringExpiresAt = DateTimeOffset.UtcNow.AddHours(2);

        var seconds = PayjoinUriSessionService.ToExpirationSeconds(monitoringExpiresAt);

        // ~2 hours (7200s); allow a small tolerance for elapsed wall-clock during the call.
        Assert.InRange(seconds, 7140UL, 7200UL);
    }

    [Fact]
    public void ExpirationFloorsAtOneSecondWhenWindowAlreadyElapsed()
    {
        var monitoringExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var seconds = PayjoinUriSessionService.ToExpirationSeconds(monitoringExpiresAt);

        Assert.Equal(1UL, seconds);
    }

    [Fact]
    public void ExpirationClampsToUInt32Max()
    {
        var monitoringExpiresAt = DateTimeOffset.UtcNow.AddSeconds((double)uint.MaxValue + 1_000_000d);

        var seconds = PayjoinUriSessionService.ToExpirationSeconds(monitoringExpiresAt);

        Assert.Equal(uint.MaxValue, seconds);
    }
}
