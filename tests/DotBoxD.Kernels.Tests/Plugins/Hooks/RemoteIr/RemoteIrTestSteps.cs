using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

internal static class RemoteIrTestSteps
{
    private const string CurrentName = "$dotboxd.current";

    public static IRFunc<TInput, TOutput> Ir<TInput, TOutput>(LoweredPipelineStepKind kind)
        => IRFunc<TInput, TOutput>.FromStep(Step(kind, typeof(TInput), typeof(TOutput)));

    public static IRFunc<TInput, TContext, TOutput> Ir<TInput, TContext, TOutput>(LoweredPipelineStepKind kind)
        => IRFunc<TInput, TContext, TOutput>.FromStep(Step(kind, typeof(TInput), typeof(TOutput)));

    private static LoweredPipelineStep Step(LoweredPipelineStepKind kind, Type input, Type output)
    {
        var span = new SourceSpan(1, 1);
        Expression value = kind == LoweredPipelineStepKind.Filter
            ? new LiteralExpression(SandboxValue.FromBool(true), span)
            : new VariableExpression(CurrentName, span);

        return new LoweredPipelineStep(
            kind,
            TypeName(input),
            TypeName(output),
            [new Parameter(CurrentName, KernelRpcMarshaller.SandboxTypeOf(input))],
            [],
            value,
            [],
            []);
    }

    private static string TypeName(Type type)
        => type.FullName ?? type.Name;
}
