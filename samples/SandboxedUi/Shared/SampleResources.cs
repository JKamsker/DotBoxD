using DotBoxD.UI.Runtime;

namespace Examples.SandboxedUi.Host;

internal static class SampleResources
{
    public static UiResourceCatalog Images() => new(new Dictionary<string, UiImageResource>(StringComparer.Ordinal)
    {
        ["sample.icon"] = UiImageResource.FromRgba(2, 2,
            [40, 120, 220, 255, 40, 120, 220, 255, 40, 120, 220, 255, 40, 120, 220, 255])
    });
}
