using System.Buffers.Binary;
using System.Globalization;

namespace DotBoxD.Services.Protocol;

/// <summary>
/// Optional, bounded negotiation over an authenticated duplex stream. Both endpoints must opt in.
/// No fallback is attempted after failure; close the stream. Raw legacy peers omit this preamble.
/// </summary>
public static class RpcProtocolNegotiation
{
    public const int PreambleSize = 68;
    private const uint Magic = 0x31584244; // DBX1; also an invalid legacy frame length.

    /// <summary>Encodes the fixed, little-endian connection offer without serializer dependencies.</summary>
    public static byte[] Encode(RpcProtocolOffer offer)
    {
        Validate(offer);
        var bytes = new byte[PreambleSize];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4), 1); // Preamble format.
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), offer.MinimumVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), offer.MaximumVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), offer.CodecId);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(16), offer.SupportedFeatures);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(24), offer.RequiredFeatures);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(32), offer.MaximumFrameSize);
        if (offer.ContractFingerprint is { } fingerprint)
        {
            for (var index = 0; index < 32; index++)
            {
                bytes[36 + index] = byte.Parse(fingerprint.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
        }
        return bytes;
    }

    /// <summary>Decodes an offer, rejecting unknown preamble formats and nonzero reserved fields.</summary>
    public static RpcProtocolOffer Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PreambleSize || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4)) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(10)) != 0)
        {
            throw new InvalidDataException("Unsupported DotBoxD connection preamble.");
        }
        var fingerprint = string.Concat(bytes.Slice(36).ToArray().Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        var offer = new RpcProtocolOffer
        {
            MinimumVersion = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6)),
            MaximumVersion = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8)),
            CodecId = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12)),
            SupportedFeatures = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)),
            RequiredFeatures = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24)),
            MaximumFrameSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(32)),
            ContractFingerprint = fingerprint.All(c => c == '0') ? null : fingerprint
        };
        Validate(offer);
        return offer;
    }

    /// <summary>Pure negotiation primitive, also usable with an application-owned handshake transport.</summary>
    public static RpcProtocolAgreement Negotiate(RpcProtocolOffer local, RpcProtocolOffer remote)
    {
        Validate(local);
        Validate(remote);
        var version = Math.Min(local.MaximumVersion, remote.MaximumVersion);
        if (version < Math.Max(local.MinimumVersion, remote.MinimumVersion))
        {
            throw new InvalidDataException("No common DotBoxD wire protocol version.");
        }
        if (local.CodecId != remote.CodecId)
        {
            throw new InvalidDataException("DotBoxD codec mismatch.");
        }
        if ((local.RequiredFeatures & ~remote.SupportedFeatures) != 0 ||
            (remote.RequiredFeatures & ~local.SupportedFeatures) != 0)
        {
            throw new InvalidDataException("A required DotBoxD feature is unsupported.");
        }
        if (!string.Equals(local.ContractFingerprint, remote.ContractFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("DotBoxD RPC contract fingerprint mismatch.");
        }
        return new RpcProtocolAgreement(version, local.CodecId,
            local.SupportedFeatures & remote.SupportedFeatures,
            Math.Min(local.MaximumFrameSize, remote.MaximumFrameSize));
    }

    /// <summary>
    /// Exchanges offers with a finite overall timeout (default 10 seconds). The caller owns the stream,
    /// must close it on failure, and must apply the returned frame limit to its channel.
    /// </summary>
    public static async Task<RpcProtocolAgreement> ExchangeAsync(
        Stream stream, RpcProtocolOffer offer, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        var bytes = Encode(offer);
        var duration = timeout ?? TimeSpan.FromSeconds(10);
        if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(duration);
        await stream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
        await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
        var remote = new byte[PreambleSize];
        var offset = 0;
        while (offset < remote.Length)
        {
            var count = await stream.ReadAsync(remote.AsMemory(offset), deadline.Token).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("Incomplete DotBoxD connection preamble.");
            }
            offset += count;
        }
        return Negotiate(offer, Decode(remote));
    }

    private static void Validate(RpcProtocolOffer offer)
    {
        if (offer is null)
        {
            throw new ArgumentNullException(nameof(offer));
        }
        if (offer.MinimumVersion == 0 || offer.MinimumVersion > offer.MaximumVersion || offer.CodecId == 0 ||
            offer.MaximumFrameSize < MessageFramer.HeaderSize || offer.MaximumFrameSize > MessageFramer.MaxMessageSize ||
            (offer.RequiredFeatures & ~offer.SupportedFeatures) != 0)
        {
            throw new InvalidDataException("Invalid DotBoxD protocol offer.");
        }
        ValidateFingerprint(offer.ContractFingerprint);
    }

    private static void ValidateFingerprint(string? fingerprint)
    {
        if (fingerprint is not null &&
            (fingerprint.Length != 64 || fingerprint.Any(c => !Uri.IsHexDigit(c)) || fingerprint.All(c => c == '0')))
        {
            throw new InvalidDataException("Expected a nonzero SHA-256 contract fingerprint.");
        }
    }
}
