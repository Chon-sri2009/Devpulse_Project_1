using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace MiniProject_Everything_1.Services;

public static class NetworkCalculatorService
{
    public static Ipv4SubnetResult CalculateIpv4(string address, int prefix)
    {
        if (!IPAddress.TryParse(address?.Trim(), out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Enter a valid IPv4 address, for example 192.168.10.25.");
        if (prefix is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(prefix), "IPv4 prefix must be between 0 and 32.");

        var ip = ToUInt32(parsed);
        var mask = PrefixMask(prefix);
        var network = ip & mask;
        var broadcast = network | ~mask;
        var total = 1UL << (32 - prefix);
        ulong usable;
        uint first;
        uint last;
        string rangeNote;

        if (prefix == 32)
        {
            usable = 1; first = last = network; rangeNote = "Single-host route";
        }
        else if (prefix == 31)
        {
            usable = 2; first = network; last = broadcast; rangeNote = "RFC 3021 point-to-point range";
        }
        else
        {
            usable = total - 2; first = network + 1; last = broadcast - 1; rangeNote = "Network and broadcast addresses excluded";
        }

        return new(
            FromUInt32(ip).ToString(), prefix, FromUInt32(network).ToString(), FromUInt32(broadcast).ToString(),
            FromUInt32(mask).ToString(), FromUInt32(~mask).ToString(), FromUInt32(first).ToString(),
            FromUInt32(last).ToString(), total, usable, ClassifyIpv4(ip),
            string.Join('.', parsed.GetAddressBytes().Reverse()) + ".in-addr.arpa",
            Convert.ToHexString(parsed.GetAddressBytes()), Binary(parsed.GetAddressBytes()), Binary(FromUInt32(mask).GetAddressBytes()),
            ip - network, prefix == 0 ? null : network >= (1UL << (32 - prefix)) ? FromUInt32(network - (uint)(1UL << (32 - prefix))).ToString() : null,
            broadcast == uint.MaxValue ? null : FromUInt32(broadcast + 1).ToString(), rangeNote);
    }

    public static Ipv4SubnetResult CalculateIpv4(string cidr)
    {
        var parts = (cidr ?? "").Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix))
            throw new ArgumentException("Enter IPv4 CIDR notation, for example 10.20.30.40/24.");
        return CalculateIpv4(parts[0], prefix);
    }

    public static MaskConversionResult ConvertMask(string value)
    {
        var input = (value ?? "").Trim();
        int prefix;
        if (input.StartsWith('/')) input = input[1..];
        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
        {
            if (prefix is < 0 or > 32) throw new ArgumentException("CIDR prefix must be between 0 and 32.");
        }
        else
        {
            if (!IPAddress.TryParse(input, out var maskAddress) || maskAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("Enter a prefix such as /24 or a contiguous subnet mask such as 255.255.255.0.");
            var mask = ToUInt32(maskAddress);
            prefix = BitOperations.LeadingZeroCount(~mask);
            if (mask != PrefixMask(prefix)) throw new ArgumentException("Subnet mask bits must be contiguous.");
        }

        var numericMask = PrefixMask(prefix);
        var addresses = 1UL << (32 - prefix);
        var traditionalUsable = prefix >= 31 ? addresses : addresses - 2;
        return new(prefix, FromUInt32(numericMask).ToString(), FromUInt32(~numericMask).ToString(), addresses, traditionalUsable, Binary(FromUInt32(numericMask).GetAddressBytes()));
    }

    public static Ipv6PrefixResult CalculateIpv6(string address, int prefix)
    {
        if (!IPAddress.TryParse(address?.Trim(), out var parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Enter a valid IPv6 address, for example 2001:db8:10::25.");
        if (prefix is < 0 or > 128) throw new ArgumentOutOfRangeException(nameof(prefix), "IPv6 prefix must be between 0 and 128.");

        var original = parsed.GetAddressBytes();
        var network = original.ToArray();
        var last = original.ToArray();
        for (var bit = prefix; bit < 128; bit++)
        {
            var index = bit / 8;
            var flag = (byte)(1 << (7 - bit % 8));
            network[index] &= (byte)~flag;
            last[index] |= flag;
        }
        var networkAddress = new IPAddress(network);
        var lastAddress = new IPAddress(last);
        var count = BigInteger.One << (128 - prefix);
        return new(parsed.ToString(), Expanded(original), prefix, networkAddress.ToString(), lastAddress.ToString(),
            count.ToString(CultureInfo.InvariantCulture), ClassifyIpv6(original), Ipv6ReverseDns(original));
    }

    public static RouteSummaryResult SummarizeIpv4(IEnumerable<string> inputs)
    {
        var ranges = new List<(uint First, uint Last)>();
        foreach (var raw in inputs)
        {
            var value = raw.Trim();
            if (value.Length == 0) continue;
            if (value.Contains('/'))
            {
                var subnet = CalculateIpv4(value);
                ranges.Add((ToUInt32(IPAddress.Parse(subnet.Network)), ToUInt32(IPAddress.Parse(subnet.Broadcast))));
            }
            else
            {
                if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException($"'{value}' is not a valid IPv4 address or CIDR block.");
                var number = ToUInt32(address);
                ranges.Add((number, number));
            }
        }
        if (ranges.Count == 0) throw new ArgumentException("Enter at least one IPv4 address or CIDR block.");

        var first = ranges.Min(x => x.First);
        var last = ranges.Max(x => x.Last);
        var prefix = BitOperations.LeadingZeroCount(first ^ last);
        var mask = PrefixMask(prefix);
        var network = first & mask;
        var broadcast = network | ~mask;
        var covered = 1UL << (32 - prefix);
        ulong supplied = 0;
        var mergedStart = ranges.OrderBy(x => x.First).First().First;
        var mergedEnd = ranges.OrderBy(x => x.First).First().Last;
        foreach (var item in ranges.OrderBy(x => x.First).Skip(1))
        {
            if ((ulong)item.First <= (ulong)mergedEnd + 1)
            {
                mergedEnd = Math.Max(mergedEnd, item.Last);
                continue;
            }
            supplied += (ulong)mergedEnd - mergedStart + 1;
            mergedStart = item.First;
            mergedEnd = item.Last;
        }
        supplied += (ulong)mergedEnd - mergedStart + 1;
        return new($"{FromUInt32(network)}/{prefix}", FromUInt32(network).ToString(), FromUInt32(broadcast).ToString(),
            prefix, covered, supplied, covered - Math.Min(covered, supplied), ranges.Count);
    }

    public static AddressRangeResult CalculateIpv4Range(string start, string end)
    {
        if (!IPAddress.TryParse(start?.Trim(), out var firstAddress) || firstAddress.AddressFamily != AddressFamily.InterNetwork ||
            !IPAddress.TryParse(end?.Trim(), out var lastAddress) || lastAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Enter valid IPv4 start and end addresses.");
        var first = ToUInt32(firstAddress);
        var last = ToUInt32(lastAddress);
        if (last < first) throw new ArgumentException("The ending address must not be lower than the starting address.");
        var count = (ulong)last - first + 1;
        var prefix = BitOperations.LeadingZeroCount(first ^ last);
        var exactCidr = first == (first & PrefixMask(prefix)) && count == (1UL << (32 - prefix)) ? $"{firstAddress}/{prefix}" : null;
        return new(firstAddress.ToString(), lastAddress.ToString(), count, exactCidr);
    }

    public static IReadOnlyList<VlsmAllocation> PlanVlsm(string parentCidr, IEnumerable<VlsmRequest> requests)
    {
        var parent = CalculateIpv4(parentCidr);
        var parentStart = ToUInt32(IPAddress.Parse(parent.Network));
        var parentEnd = ToUInt32(IPAddress.Parse(parent.Broadcast));
        var prepared = requests.Select((request, index) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException($"Subnet {index + 1} needs a name.");
            if (request.Hosts < 1) throw new ArgumentException($"'{request.Name}' must request at least one host.");
            var needed = (ulong)request.Hosts + 2;
            var size = NextPowerOfTwo(needed);
            var prefix = 32 - BitOperations.Log2(size);
            return (Name: request.Name.Trim(), request.Hosts, Size: size, Prefix: prefix, Index: index);
        }).OrderByDescending(x => x.Size).ThenBy(x => x.Index).ToList();
        if (prepared.Count == 0) throw new ArgumentException("Enter at least one subnet requirement.");
        if (prepared.Count > 256) throw new ArgumentException("A plan is limited to 256 subnet requirements.");

        ulong cursor = parentStart;
        var allocations = new List<VlsmAllocation>(prepared.Count);
        foreach (var item in prepared)
        {
            var aligned = (cursor + item.Size - 1) / item.Size * item.Size;
            var last = aligned + item.Size - 1;
            if (aligned > uint.MaxValue || last > parentEnd)
                throw new InvalidOperationException($"The requested subnets do not fit inside {parent.Network}/{parent.Prefix}. '{item.Name}' could not be allocated.");
            var network = (uint)aligned;
            var broadcast = (uint)last;
            allocations.Add(new(item.Name, item.Hosts, item.Prefix, $"{FromUInt32(network)}/{item.Prefix}", FromUInt32(network).ToString(),
                FromUInt32(network + 1).ToString(), FromUInt32(broadcast - 1).ToString(), FromUInt32(broadcast).ToString(), item.Size - 2, item.Size));
            cursor = last + 1;
        }
        return allocations;
    }

    public static TransferCalculation CalculateTransfer(double amount, string amountUnit, double speed, string speedUnit, double overheadPercent, double rttMs)
    {
        if (!double.IsFinite(amount) || amount <= 0 || !double.IsFinite(speed) || speed <= 0)
            throw new ArgumentException("Data amount and link speed must be positive numbers.");
        if (!double.IsFinite(overheadPercent) || overheadPercent is < 0 or >= 100)
            throw new ArgumentException("Protocol overhead must be between 0 and 99.99 percent.");
        if (!double.IsFinite(rttMs) || rttMs < 0) throw new ArgumentException("Round-trip latency cannot be negative.");
        var bytes = amount * UnitMultiplier(amountUnit, false);
        var bitsPerSecond = speed * UnitMultiplier(speedUnit, true);
        var effectiveBitsPerSecond = bitsPerSecond * (1 - overheadPercent / 100d);
        var seconds = bytes * 8 / effectiveBitsPerSecond;
        var bdpBytes = bitsPerSecond * rttMs / 8_000d;
        return new(bytes, bitsPerSecond, effectiveBitsPerSecond, seconds, FormatDuration(seconds), bdpBytes, bdpBytes / 1024d,
            Math.Ceiling(bytes / 1460d));
    }

    public static MtuCalculation CalculateMtu(int mtu, int ipVersion, int tcpOptionBytes)
    {
        if (mtu is < 68 or > 65535) throw new ArgumentException("MTU must be between 68 and 65,535 bytes.");
        if (ipVersion is not (4 or 6)) throw new ArgumentException("IP version must be 4 or 6.");
        if (tcpOptionBytes is < 0 or > 40 || tcpOptionBytes % 4 != 0) throw new ArgumentException("TCP option bytes must be 0–40 and divisible by four.");
        var ipHeader = ipVersion == 4 ? 20 : 40;
        var tcpHeader = 20 + tcpOptionBytes;
        var mss = mtu - ipHeader - tcpHeader;
        var udpPayload = mtu - ipHeader - 8;
        if (mss < 0 || udpPayload < 0) throw new ArgumentException("The MTU is too small for the selected headers.");
        return new(mtu, ipVersion, ipHeader, tcpHeader, mss, udpPayload, mtu + 18);
    }

    private static ulong NextPowerOfTwo(ulong value)
    {
        if (value <= 1) return 1;
        return 1UL << (BitOperations.Log2(value - 1) + 1);
    }

    private static double UnitMultiplier(string unit, bool rate) => (unit ?? "").ToUpperInvariant() switch
    {
        "KB" => 1_000d,
        "MB" => 1_000_000d,
        "GB" => 1_000_000_000d,
        "TB" => 1_000_000_000_000d,
        "KIB" => 1024d,
        "MIB" => 1_048_576d,
        "GIB" => 1_073_741_824d,
        "TIB" => 1_099_511_627_776d,
        "KBPS" when rate => 1_000d,
        "MBPS" when rate => 1_000_000d,
        "GBPS" when rate => 1_000_000_000d,
        "TBPS" when rate => 1_000_000_000_000d,
        _ => throw new ArgumentException($"Unsupported {(rate ? "speed" : "data")} unit '{unit}'.")
    };

    private static string FormatDuration(double seconds)
    {
        if (seconds < 1) return $"{seconds * 1000:F1} ms";
        var span = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span:hh\\:mm\\:ss}";
        if (span.TotalHours >= 1) return span.ToString("h\\:mm\\:ss", CultureInfo.InvariantCulture);
        return span.ToString("m\\:ss\\.fff", CultureInfo.InvariantCulture);
    }

    private static uint PrefixMask(int prefix) => prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
    private static IPAddress FromUInt32(uint value) => new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    private static string Binary(byte[] bytes) => string.Join('.', bytes.Select(x => Convert.ToString(x, 2).PadLeft(8, '0')));
    private static string Expanded(byte[] bytes) => string.Join(':', Enumerable.Range(0, 8).Select(i => $"{bytes[i * 2]:x2}{bytes[i * 2 + 1]:x2}"));
    private static string Ipv6ReverseDns(byte[] bytes) => string.Join('.', Convert.ToHexString(bytes).ToLowerInvariant().Reverse()) + ".ip6.arpa";

    private static string ClassifyIpv4(uint ip)
    {
        if ((ip & 0xff000000) == 0x0a000000 || (ip & 0xfff00000) == 0xac100000 || (ip & 0xffff0000) == 0xc0a80000) return "Private (RFC 1918)";
        if ((ip & 0xff000000) == 0x7f000000) return "Loopback";
        if ((ip & 0xffff0000) == 0xa9fe0000) return "Link-local (APIPA)";
        if ((ip & 0xffc00000) == 0x64400000) return "Carrier-grade NAT";
        if ((ip & 0xf0000000) == 0xe0000000) return "Multicast";
        if ((ip & 0xf0000000) == 0xf0000000) return "Reserved";
        if ((ip & 0xffffff00) == 0xc0000200 || (ip & 0xffffff00) == 0xc6336400 || (ip & 0xffffff00) == 0xcb007100) return "Documentation";
        if ((ip & 0xff000000) == 0 || ip == uint.MaxValue) return "Reserved / this network";
        return "Public unicast";
    }

    private static string ClassifyIpv6(byte[] bytes)
    {
        if (bytes.All(x => x == 0)) return "Unspecified";
        if (bytes[..15].All(x => x == 0) && bytes[15] == 1) return "Loopback";
        if ((bytes[0] & 0xfe) == 0xfc) return "Unique local";
        if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return "Link-local";
        if (bytes[0] == 0xff) return "Multicast";
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return "Documentation";
        if ((bytes[0] & 0xe0) == 0x20) return "Global unicast";
        return "Special-purpose / reserved";
    }
}

public sealed record Ipv4SubnetResult(string Address, int Prefix, string Network, string Broadcast, string SubnetMask,
    string WildcardMask, string FirstUsable, string LastUsable, ulong TotalAddresses, ulong UsableHosts, string AddressType,
    string ReverseDns, string Hexadecimal, string BinaryAddress, string BinaryMask, uint HostNumber, string? PreviousSubnet,
    string? NextSubnet, string RangeNote);
public sealed record MaskConversionResult(int Prefix, string SubnetMask, string WildcardMask, ulong TotalAddresses, ulong UsableHosts, string BinaryMask);
public sealed record Ipv6PrefixResult(string CompressedAddress, string ExpandedAddress, int Prefix, string Network, string LastAddress,
    string TotalAddresses, string AddressType, string ReverseDns);
public sealed record RouteSummaryResult(string Cidr, string Network, string Broadcast, int Prefix, ulong CoveredAddresses,
    ulong SuppliedAddresses, ulong AdditionalAddresses, int InputCount);
public sealed record AddressRangeResult(string Start, string End, ulong Count, string? ExactCidr);
public sealed record VlsmRequest(string Name, int Hosts);
public sealed record VlsmAllocation(string Name, int RequestedHosts, int Prefix, string Cidr, string Network, string FirstUsable,
    string LastUsable, string Broadcast, ulong Capacity, ulong TotalAddresses);
public sealed record TransferCalculation(double Bytes, double LinkBitsPerSecond, double EffectiveBitsPerSecond, double Seconds,
    string FriendlyDuration, double BandwidthDelayProductBytes, double BandwidthDelayProductKiB, double ApproximateTcpSegments);
public sealed record MtuCalculation(int Mtu, int IpVersion, int IpHeaderBytes, int TcpHeaderBytes, int TcpMss, int UdpPayload, int EthernetFrameBytes);
