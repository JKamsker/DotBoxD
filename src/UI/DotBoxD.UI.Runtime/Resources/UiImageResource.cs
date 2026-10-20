using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

/// <summary>Trusted host-owned RGBA pixels; no plugin bytes are decoded as an image.</summary>
public sealed class UiImageResource
{
    private UiImageResource(int width, int height, ImmutableArray<byte> rgba)
    { Width = width; Height = height; Rgba = rgba; }

    public int Width { get; }
    public int Height { get; }
    public ImmutableArray<byte> Rgba { get; }

    public static UiImageResource FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height * 4 != rgba.Length)
        { throw new ArgumentException("RGBA data must contain exactly four bytes per pixel.", nameof(rgba)); }
        return new UiImageResource(width, height, [.. rgba]);
    }

    /// <summary>Encodes validated pixels as PNG, without file paths, network or image decoders.</summary>
    public byte[] ToPng() => UiPngEncoder.Encode(this);
}
