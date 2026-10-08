using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

/// <summary>Explicit host grants. The catalog is scoped by the host to a plugin/session.</summary>
public sealed class UiResourceCatalog
{
    private readonly ImmutableDictionary<string, UiImageResource> _images;
    public static UiResourceCatalog Empty { get; } = new([]);

    public UiResourceCatalog(IEnumerable<KeyValuePair<string, UiImageResource>> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        _images = images.ToImmutableDictionary(StringComparer.Ordinal);
        if (_images.Any(p => p.Value is null || !ValidHandle(p.Key)))
        { throw new ArgumentException("Resource grants require opaque handles and trusted RGBA images.", nameof(images)); }
    }

    public UiImageResource GetImage(string handle) => _images.TryGetValue(handle, out var image)
        ? image : throw new UiValidationException("Image resource was not granted by the trusted host.");

    public void Validate(UiPackage package, UiPolicy policy)
    {
        UiPackageValidator.Validate(package, policy);
        long total = 0;
        foreach (var resource in package.Resources)
        {
            var image = GetImage(resource.Handle);
            if ((long)image.Width * image.Height > policy.MaxImagePixels || image.Rgba.Length > policy.MaxResourceBytes)
            { throw new UiValidationException("Image exceeds the host pixel or resource byte limit."); }
            total += image.Rgba.Length;
        }
        if (total > policy.MaxTotalResourceBytes)
        { throw new UiValidationException("Images exceed the host total resource byte limit."); }
    }

    private static bool ValidHandle(string handle) => handle.Length is > 0 and <= 128 &&
        handle.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
