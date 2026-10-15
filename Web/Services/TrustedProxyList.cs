using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace RustPlusWeb.Services;

/// <summary>
/// The set of reverse proxies whose X-Forwarded-For header is believed.
///
/// Behind a proxy every request reaches the app from the proxy's address, so anything keyed on
/// the connection address (the login rate limit) sees one client for the whole instance. The
/// header carries the real address, but anyone can send one, so it is only honoured when the
/// direct peer is a configured proxy, and then only the right-most entry that is not itself a
/// trusted proxy is used. Entries further left were supplied by the caller and are ignored.
/// Anything that cannot be understood falls back to the peer address, which is the safe side:
/// at worst a user shares a limit with the others behind the same proxy.
/// </summary>
public sealed class TrustedProxyList
{
    private const int MaxForwardedEntries = 32;

    private readonly IReadOnlyList<IPNetwork> _networks;

    private TrustedProxyList(IReadOnlyList<IPNetwork> networks) => _networks = networks;

    public static TrustedProxyList Empty { get; } = new(Array.Empty<IPNetwork>());

    public bool IsEmpty => _networks.Count == 0;

    /// <summary>
    /// Parses a comma, semicolon or space separated list of IP addresses and CIDR ranges
    /// (for example <c>172.18.0.0/16, 10.0.0.5</c>). A typo throws instead of being skipped,
    /// because a silently ignored entry would put the rate limit back to one shared bucket.
    /// </summary>
    public static TrustedProxyList Parse(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return Empty;

        var networks = new List<IPNetwork>();
        var invalid = new List<string>();
        var separators = new[] { ',', ';', ' ' };

        foreach (var entry in configuration.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseEntry(entry, out var network)) networks.Add(network);
            else invalid.Add(entry);
        }

        if (invalid.Count > 0)
        {
            throw new FormatException(
                $"WEB_TRUSTED_PROXIES contains invalid entries: {string.Join(", ", invalid)}. " +
                "Use IP addresses or CIDR ranges such as 172.18.0.0/16.");
        }

        return new TrustedProxyList(networks);
    }

    public bool IsTrusted(IPAddress? address)
    {
        if (address == null) return false;
        address = Normalize(address);

        foreach (var network in _networks)
        {
            if (network.Contains(address)) return true;
        }

        return false;
    }

    /// <summary>The address to treat as the client for a request that arrived from <paramref name="peer"/>.</summary>
    public IPAddress? ResolveClient(IPAddress? peer, string? forwardedFor)
    {
        if (!IsTrusted(peer) || string.IsNullOrWhiteSpace(forwardedFor))
            return peer;

        var entries = forwardedFor.Split(',', StringSplitOptions.TrimEntries);
        if (entries.Length > MaxForwardedEntries)
            return peer;

        IPAddress? leftmost = null;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            if (!TryParseForwarded(entries[i], out var address))
                return peer;

            if (!IsTrusted(address))
                return address;

            leftmost = address;
        }

        // Every hop was one of our own proxies, so the left-most is the best answer there is.
        return leftmost ?? peer;
    }

    private static bool TryParseEntry(string entry, out IPNetwork network)
    {
        network = default;

        var slash = entry.IndexOf('/');
        var addressText = slash < 0 ? entry : entry[..slash];
        if (!IPAddress.TryParse(addressText, out var parsed))
            return false;

        if (slash < 0)
        {
            var single = Normalize(parsed);
            var singleBits = single.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            network = new IPNetwork(single, singleBits);
            return true;
        }

        // Ranges are written in plain IPv4 or IPv6 form, not as IPv4-mapped IPv6.
        if (parsed.IsIPv4MappedToIPv6)
            return false;

        var bits = parsed.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (!int.TryParse(entry[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) ||
            prefix > bits)
            return false;

        // A range written with host bits set (172.18.0.5/16) means the network around it.
        // The IPNetwork constructor rejects that, so mask the address first.
        network = new IPNetwork(MaskToPrefix(parsed, prefix), prefix);
        return true;
    }

    private static IPAddress MaskToPrefix(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        for (int i = 0; i < bytes.Length; i++)
        {
            int bitsInByte = Math.Clamp(prefix - i * 8, 0, 8);
            int mask = bitsInByte == 0 ? 0 : (0xFF << (8 - bitsInByte)) & 0xFF;
            bytes[i] = (byte)(bytes[i] & mask);
        }

        return new IPAddress(bytes);
    }

    private static bool TryParseForwarded(string value, out IPAddress address)
    {
        address = IPAddress.None;
        value = value.Trim();
        if (value.Length == 0) return false;

        if (value[0] == '[')
        {
            // [2001:db8::1]:443
            var end = value.IndexOf(']');
            if (end < 0) return false;
            value = value[1..end];
        }
        else if (value.Count(c => c == ':') == 1)
        {
            // 203.0.113.9:51234
            value = value[..value.IndexOf(':')];
        }

        if (!IPAddress.TryParse(value, out var parsed))
            return false;

        address = Normalize(parsed);
        return true;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
