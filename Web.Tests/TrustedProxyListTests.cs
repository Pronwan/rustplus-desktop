using System.Net;
using RustPlusWeb.Services;
using Xunit;

public sealed class TrustedProxyListTests
{
    private static IPAddress Ip(string value) => IPAddress.Parse(value);

    [Fact]
    public void EmptyConfigurationTrustsNothing()
    {
        Assert.True(TrustedProxyList.Parse(null).IsEmpty);
        Assert.True(TrustedProxyList.Parse("  ").IsEmpty);
        Assert.False(TrustedProxyList.Empty.IsTrusted(Ip("127.0.0.1")));
    }

    [Fact]
    public void InvalidEntriesFailStartupInsteadOfBeingSkipped()
    {
        var error = Assert.Throws<FormatException>(() => TrustedProxyList.Parse("10.0.0.1, proxy.local, 10.0.0.0/33"));

        Assert.Contains("proxy.local", error.Message);
        Assert.Contains("10.0.0.0/33", error.Message);
    }

    [Fact]
    public void AddressesAndCidrRangesAreTrusted()
    {
        var list = TrustedProxyList.Parse("192.168.1.10; 172.18.0.0/16 2001:db8::/32");

        Assert.True(list.IsTrusted(Ip("192.168.1.10")));
        Assert.False(list.IsTrusted(Ip("192.168.1.11")));
        Assert.True(list.IsTrusted(Ip("172.18.44.7")));
        Assert.False(list.IsTrusted(Ip("172.19.0.1")));
        Assert.True(list.IsTrusted(Ip("2001:db8::5")));
        Assert.False(list.IsTrusted(Ip("2001:db9::5")));
    }

    [Fact]
    public void RangeWrittenWithHostBitsMeansTheSurroundingNetwork()
    {
        var list = TrustedProxyList.Parse("172.18.0.5/16");

        Assert.True(list.IsTrusted(Ip("172.18.200.1")));
    }

    [Fact]
    public void IPv4MappedPeerMatchesAnIPv4Entry()
    {
        var list = TrustedProxyList.Parse("10.0.0.5");

        Assert.True(list.IsTrusted(Ip("::ffff:10.0.0.5")));
    }

    [Fact]
    public void HeaderFromAnUntrustedPeerIsIgnored()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");

        var client = list.ResolveClient(Ip("198.51.100.20"), "203.0.113.9");

        Assert.Equal(Ip("198.51.100.20"), client);
    }

    [Fact]
    public void TrustedPeerYieldsTheForwardedClient()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");

        Assert.Equal(Ip("203.0.113.9"), list.ResolveClient(Ip("10.0.0.1"), "203.0.113.9"));
    }

    [Fact]
    public void EntriesTheCallerPrependedAreNotBelieved()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");

        // The caller sent 198.51.100.1 itself; the proxy appended the address it really saw.
        var client = list.ResolveClient(Ip("10.0.0.1"), "198.51.100.1, 203.0.113.9");

        Assert.Equal(Ip("203.0.113.9"), client);
    }

    [Fact]
    public void ChainsThroughSeveralTrustedProxiesAreWalked()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");

        var client = list.ResolveClient(Ip("10.0.0.1"), "203.0.113.9, 10.0.0.2");

        Assert.Equal(Ip("203.0.113.9"), client);
    }

    [Fact]
    public void AllTrustedChainReturnsTheLeftmost()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");

        Assert.Equal(Ip("10.0.0.7"), list.ResolveClient(Ip("10.0.0.1"), "10.0.0.7, 10.0.0.2"));
    }

    [Fact]
    public void AnythingUnparseableFallsBackToThePeer()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");
        var peer = Ip("10.0.0.1");

        Assert.Equal(peer, list.ResolveClient(peer, "203.0.113.9, not-an-ip"));
        Assert.Equal(peer, list.ResolveClient(peer, "unknown"));
        Assert.Equal(peer, list.ResolveClient(peer, null));
        Assert.Equal(peer, list.ResolveClient(peer, ""));
    }

    [Fact]
    public void PortsAndBracketedIPv6AreUnderstood()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");
        var peer = Ip("10.0.0.1");

        Assert.Equal(Ip("203.0.113.9"), list.ResolveClient(peer, "203.0.113.9:51234"));
        Assert.Equal(Ip("2001:db8::7"), list.ResolveClient(peer, "[2001:db8::7]:443"));
        Assert.Equal(Ip("2001:db8::7"), list.ResolveClient(peer, "2001:db8::7"));
    }

    [Fact]
    public void AbsurdlyLongChainsAreNotWalked()
    {
        var list = TrustedProxyList.Parse("10.0.0.0/8");
        var peer = Ip("10.0.0.1");
        var header = string.Join(", ", Enumerable.Repeat("203.0.113.9", 100));

        Assert.Equal(peer, list.ResolveClient(peer, header));
    }
}
