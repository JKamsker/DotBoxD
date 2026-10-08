using System.Buffers.Binary;
using System.IO.Compression;
using DotBoxD.UI;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Resources;

public sealed class UiResourceValidationTests
{
    [Theory]
    [InlineData("https://example.test/image")]
    [InlineData("file:///etc/passwd")]
    [InlineData("../private")]
    [InlineData("/trusted/path")]
    [InlineData("data:image/svg+xml,<svg onload=alert(1)>")]
    public void Resource_handles_cannot_embed_paths_URLs_or_executable_data(string handle)
    {
        var builder = new UiBuilder();
        Assert.Throws<UiValidationException>(() => builder.Build(builder.Image(builder.Resource(handle))));
    }

    [Fact]
    public void Missing_dynamic_and_over_limit_image_references_fail_closed()
    {
        var builder = new UiBuilder();
        var package = builder.Build(builder.Image(builder.Resource("host.icon")));
        Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(package with { Resources = [] }, new UiPolicy()));
        Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(package, new UiPolicy { MaxResources = 0 }));
        var dynamic = package with
        {
            State = [new(1, UiValue.FromInt32(1))],
            Nodes = [package.Nodes[0] with { Properties = [new(UiPropertyId.Resource, StateSlotId: 1)] }]
        };
        Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(dynamic, new UiPolicy()));
        var json = UiPackageJson.Export(package, new UiPolicy());
        Assert.Equal(UiPackageJson.ComputeHash(package, new UiPolicy()), UiPackageJson.ComputeHash(UiPackageJson.Import(json, new UiPolicy()), new UiPolicy()));
    }

    [Fact]
    public void PNG_encoding_preserves_pixels_and_dimensions_without_decoder_input()
    {
        var image = UiImageResource.FromRgba(2, 1, [1, 2, 3, 255, 4, 5, 6, 128]);
        var png = image.ToPng();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        var compressedLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        using var compressed = new MemoryStream(png, 41, compressedLength);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 255, 4, 5, 6, 128 }, raw.ToArray());
    }
}
