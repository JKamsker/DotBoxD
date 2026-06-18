using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record PluginKernelModel(
    string PluginId,
    string Namespace,
    string KernelName,
    string PackageName,
    string EventName,
    string EventParameterName,
    string ContextParameterName,
    string HandleEventParameterName,
    string HandleContextParameterName,
    EquatableArray<EventPropertyModel> EventProperties,
    EquatableArray<LiveSettingModel> LiveSettings,
    DotBoxDStatementBodyModel ShouldHandle,
    DotBoxDHandleModel? Handle,
    EquatableArray<string> ManifestEffects,
    EquatableArray<string> RequiredCapabilities,
    EquatableArray<IndexPredicateModel> IndexPredicates,
    bool IndexCoversPredicate,
    // Local-terminal (remote RunLocal) chains: the Handle entrypoint RETURNS the projected value
    // (ProjectionBody) of manifest type ProjectedType instead of performing a host send (Handle is null).
    bool LocalTerminal = false,
    string? ProjectedType = null,
    DotBoxDStatementBodyModel? ProjectionBody = null);

internal sealed record EventPropertyModel(string Name, string Type);

/// <summary>
/// One index-eligible <c>event-property &lt;op&gt; constant</c> comparison extracted from a lowered
/// <c>.Where(...)</c> chain. All fields are strings so the model stays value-equatable for incremental
/// generation; <see cref="ValueLiteral"/> is the C# literal the emitter writes for the boxed constant and
/// <see cref="Operator"/> is the <c>DotBoxD.Plugins.IndexPredicateOperator</c> member name.
/// </summary>
internal sealed record IndexPredicateModel(
    string Path,
    string Operator,
    string ValueLiteral,
    string ValueType);

internal sealed record LiveSettingModel(
    string Name,
    string Type,
    string DefaultValue,
    string? Min,
    string? Max);
