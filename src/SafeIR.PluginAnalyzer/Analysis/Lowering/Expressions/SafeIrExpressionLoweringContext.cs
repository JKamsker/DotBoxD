namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal sealed class SafeIrExpressionLoweringContext
{
    public SafeIrExpressionLoweringContext(
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? projectedElementName = null,
        SafeIrExpressionModel? projectedElement = null)
    {
        EventParameterName = eventParameterName;
        EventProperties = eventProperties;
        LiveSettings = liveSettings;
        SemanticModel = semanticModel;
        CancellationToken = cancellationToken;
        ProjectedElementName = projectedElementName;
        ProjectedElement = projectedElement;
    }

    public string EventParameterName { get; }

    public EquatableArray<EventPropertyModel> EventProperties { get; }

    public EquatableArray<LiveSettingModel> LiveSettings { get; }

    public SemanticModel SemanticModel { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// For a lowered hook chain after a <c>Select</c>: the name the downstream lambda gives the
    /// projected element, bound to its already-lowered IR (<see cref="ProjectedElement"/>). Null in
    /// event mode (kernels and pre-Select stages), where the element is the event itself.
    /// </summary>
    public string? ProjectedElementName { get; }

    public SafeIrExpressionModel? ProjectedElement { get; }
}
