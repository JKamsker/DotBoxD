namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainProjectionSlotResolver
{
    public static string? Final(IReadOnlyList<HookChainStage> stages)
    {
        for (var index = stages.Count - 1; index >= 0; index--)
        {
            if (stages[index].IsSelect)
            {
                return HookChainStageLowerer.SelectTemp(stages[index].Lambda);
            }
        }

        return null;
    }
}
