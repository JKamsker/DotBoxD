using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginKernelShapeValidation
{
    public static bool ContainsUnsupported(EquatableArray<EventPropertyModel> eventProperties)
    {
        for (var index = 0; index < eventProperties.Count; index++)
        {
            if (eventProperties[index].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }

    public static bool ContainsUnsupported(EquatableArray<LiveSettingModel> liveSettings)
    {
        for (var index = 0; index < liveSettings.Count; index++)
        {
            if (liveSettings[index].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }
}
