namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal sealed record HookChainStageIrModel(
    string HintName,
    string Namespace,
    string ClassName,
    string Kind,
    string InputType,
    string OutputType,
    string IRFuncType,
    string? IRFuncTypeParameters,
    string GeneratedAttributeSource,
    string ParameterSource,
    string ValueSource,
    EquatableArray<string> RequiredCapabilities,
    EquatableArray<string> Effects,
    HookChainStageIrInterception Interception);

internal sealed record HookChainStageIrInterception(
    string AttributeSyntax,
    string GeneratedAttributeSource,
    string ReceiverType,
    string DelegateType,
    string ReturnType,
    string MethodName,
    string MethodTypeArguments,
    string StepFullName,
    string? InterceptorTypeParameters,
    string CreateIRFuncTypeArguments,
    string SourceParameterName,
    string IRParameterName,
    string IRFuncType);
