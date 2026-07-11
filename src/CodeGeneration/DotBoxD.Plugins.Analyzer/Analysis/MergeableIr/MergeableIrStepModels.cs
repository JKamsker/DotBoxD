using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal sealed record MergeableIrStepCreateResult(
    MergeableIrStepModel? Step,
    PluginKernelDiagnostic? Diagnostic);

internal sealed record MergeableIrStepModel(
    string HintName,
    string Namespace,
    string ClassName,
    string Kind,
    string InputType,
    string OutputType,
    string? IRFuncType,
    string ParameterSource,
    string ValueSource,
    EquatableArray<string> RequiredCapabilities,
    EquatableArray<string> Effects,
    MergeableIrStepInterception Interception);

internal sealed record MergeableIrStepInterception(
    string AttributeSyntax,
    string ReceiverType,
    string DelegateType,
    string ReturnType,
    string MethodName,
    string MethodTypeArguments,
    string StepFullName,
    MergeableIrInterceptionKind Kind,
    string SourceParameterName,
    string? IRParameterName,
    string? IRFuncType);

internal sealed record MergeableIrMarkedLoweringCall(
    IMethodSymbol Method,
    IParameterSymbol Parameter,
    ArgumentSyntax Argument,
    MergeableIrLoweredStepKind Kind,
    ITypeSymbol InputType,
    ITypeSymbol OutputType,
    MergeableIrInterceptionKind InterceptionKind,
    IParameterSymbol? IRParameter);

internal enum MergeableIrInterceptionKind
{
    LoweredPipelineStepOverload,
    IRFuncParameter,
}

internal enum MergeableIrLoweredStepKind
{
    Filter,
    Projection,
}
