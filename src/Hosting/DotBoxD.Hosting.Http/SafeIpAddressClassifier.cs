using System.Net;

namespace DotBoxD.Hosting.Http;

internal static class SafeIpAddressClassifier
{
    private static readonly Ipv4Range[] NonGlobalIpv4Ranges =
    [
        Ipv4Range.From(0, 0, 0, 0, 0, 255, 255, 255),
        Ipv4Range.From(10, 0, 0, 0, 10, 255, 255, 255),
        Ipv4Range.From(100, 64, 0, 0, 100, 127, 255, 255),
        Ipv4Range.From(127, 0, 0, 0, 127, 255, 255, 255),
        Ipv4Range.From(169, 254, 0, 0, 169, 254, 255, 255),
        Ipv4Range.From(172, 16, 0, 0, 172, 31, 255, 255),
        Ipv4Range.From(192, 0, 0, 0, 192, 0, 0, 255),
        Ipv4Range.From(192, 0, 2, 0, 192, 0, 2, 255),
        Ipv4Range.From(192, 88, 99, 2, 192, 88, 99, 2),
        Ipv4Range.From(192, 168, 0, 0, 192, 168, 255, 255),
        Ipv4Range.From(198, 18, 0, 0, 198, 19, 255, 255),
        Ipv4Range.From(198, 51, 100, 0, 198, 51, 100, 255),
        Ipv4Range.From(203, 0, 113, 0, 203, 0, 113, 255),
        Ipv4Range.From(224, 0, 0, 0, 255, 255, 255, 255),
    ];

    public static bool IsNonGlobal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            Span<byte> mappedBytes = stackalloc byte[16];
            return !address.TryWriteBytes(mappedBytes, out _) || IsNonGlobalIpv4(mappedBytes[12..]);
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var bytesWritten))
        {
            return true;
        }

        var addressBytes = bytes[..bytesWritten];
        return bytesWritten == 4
            ? IsNonGlobalIpv4(addressBytes)
            : IsNonGlobalIpv6(address, addressBytes);
    }

    private static bool IsNonGlobalIpv4(ReadOnlySpan<byte> bytes)
    {
        var address = Ipv4Range.ToUInt32(bytes);
        foreach (var range in NonGlobalIpv4Ranges)
        {
            if (range.Contains(address))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonGlobalIpv6(IPAddress address, ReadOnlySpan<byte> bytes)
        => address.Equals(IPAddress.IPv6None) ||
           address.Equals(IPAddress.IPv6Any) ||
           address.IsIPv6LinkLocal ||
           address.IsIPv6SiteLocal ||
           HasNonGlobalIpv6Prefix(bytes);

    private static bool HasNonGlobalIpv6Prefix(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0xff ||
           (bytes[0] & 0xfe) == 0xfc ||
           (bytes[0] & 0xe0) != 0x20 ||
           IsIetfProtocolAssignment(bytes) ||
           IsDocumentation(bytes) ||
           IsDocumentation2(bytes) ||
           Is6To4(bytes);

    private static bool IsIetfProtocolAssignment(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] <= 0x01;

    private static bool IsDocumentation(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;

    private static bool IsDocumentation2(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x3f && (bytes[1] & 0xf0) == 0xf0;

    private static bool Is6To4(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x02;

    private readonly record struct Ipv4Range(uint Start, uint End)
    {
        public static Ipv4Range From(
            byte start0,
            byte start1,
            byte start2,
            byte start3,
            byte end0,
            byte end1,
            byte end2,
            byte end3)
            => new(ToUInt32(start0, start1, start2, start3), ToUInt32(end0, end1, end2, end3));

        public static uint ToUInt32(ReadOnlySpan<byte> bytes)
            => ToUInt32(bytes[0], bytes[1], bytes[2], bytes[3]);

        public bool Contains(uint address)
            => address >= Start && address <= End;

        private static uint ToUInt32(byte value0, byte value1, byte value2, byte value3)
            => ((uint)value0 << 24) | ((uint)value1 << 16) | ((uint)value2 << 8) | value3;
    }
}
