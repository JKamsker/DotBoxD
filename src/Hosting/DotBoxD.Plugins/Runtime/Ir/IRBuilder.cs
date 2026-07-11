using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins;

public static class IRBuilder
{
    public static IRBuilder<TInput> For<TInput>()
        => new(new IRExpressionBuilder());

    public static IRBuilder<TInput> For<TInput>(SourceSpan span)
        => new(new IRExpressionBuilder(span));
}

public sealed class IRBuilder<TInput>
{
    private readonly IRExpressionBuilder expression;
    private readonly SandboxType inputType;
    private readonly string inputTag;

    internal IRBuilder(IRExpressionBuilder expression)
    {
        this.expression = expression ?? throw new ArgumentNullException(nameof(expression));
        inputType = KernelRpcMarshaller.SandboxTypeOf(typeof(TInput));
        inputTag = RuntimeManifestTags.For(typeof(TInput));
    }

    public IRExpressionBuilder Expression => expression;

    public IRFunc<TInput, bool> Filter(
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => IRFunc<TInput, bool>.FromStep(FilterStep(build, requiredCapabilities, effects));

    public IRFunc<TInput, TOutput> Projection<TOutput>(
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => IRFunc<TInput, TOutput>.FromStep(ProjectionStep<TOutput>(build, requiredCapabilities, effects));

    public IRFunc<TInput, TContext, bool> FilterWithContext<TContext>(
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => IRFunc<TInput, TContext, bool>.FromStep(FilterStep(build, requiredCapabilities, effects));

    public IRFunc<TInput, TContext, TOutput> ProjectionWithContext<TContext, TOutput>(
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => IRFunc<TInput, TContext, TOutput>.FromStep(ProjectionStep<TOutput>(build, requiredCapabilities, effects));

    public LoweredPipelineStep FilterStep(
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => Step(LoweredPipelineStepKind.Filter, typeof(bool), BuildExpression(build), requiredCapabilities, effects);

    public LoweredPipelineStep ProjectionStep<TOutput>(
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => ProjectionStep(typeof(TOutput), build, requiredCapabilities, effects);

    public LoweredPipelineStep ProjectionStep(
        Type outputType,
        Func<IRExpressionBuilder, Expression> build,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
        => Step(LoweredPipelineStepKind.Projection, outputType, BuildExpression(build), requiredCapabilities, effects);

    public LoweredPipelineStep Step(
        LoweredPipelineStepKind kind,
        Type outputType,
        Expression value,
        IEnumerable<string>? requiredCapabilities = null,
        IEnumerable<string>? effects = null)
    {
        ArgumentNullException.ThrowIfNull(outputType);
        ArgumentNullException.ThrowIfNull(value);

        return new LoweredPipelineStep(
            kind,
            inputTag,
            RuntimeManifestTags.For(outputType),
            [new Parameter(IRExpressionBuilder.CurrentParameterName, inputType)],
            [],
            value,
            CopyMetadata(requiredCapabilities, nameof(requiredCapabilities)),
            CopyMetadata(effects, nameof(effects)));
    }

    private Expression BuildExpression(Func<IRExpressionBuilder, Expression> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return build(expression) ?? throw new InvalidOperationException("The IR expression builder returned null.");
    }

    private static string[] CopyMetadata(IEnumerable<string>? values, string paramName)
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(copy[i]))
            {
                throw new ArgumentException("IR metadata entries must not be empty.", paramName);
            }
        }

        return copy;
    }

    private static class RuntimeManifestTags
    {
        private static readonly Dictionary<Type, string> ExactTags = new()
        {
            [typeof(bool)] = "bool",
            [typeof(CancellationToken)] = "bool",
            [typeof(int)] = "int",
            [typeof(DateOnly)] = "int",
            [typeof(long)] = "long",
            [typeof(TimeOnly)] = "long",
            [typeof(TimeSpan)] = "long",
            [typeof(double)] = "double",
            [typeof(float)] = "double",
            [typeof(string)] = "string",
            [typeof(Guid)] = "guid",
        };

        private static readonly Type[] ListDefinitions =
        [
            typeof(List<>),
            typeof(IReadOnlyList<>),
            typeof(IList<>),
            typeof(IEnumerable<>),
            typeof(IReadOnlyCollection<>)
        ];

        private static readonly Type[] MapDefinitions =
        [
            typeof(Dictionary<,>),
            typeof(IReadOnlyDictionary<,>),
            typeof(IDictionary<,>)
        ];

        public static string For(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            _ = KernelRpcMarshaller.SandboxTypeOf(type);

            if (ExactTags.TryGetValue(type, out var tag))
            {
                return tag;
            }

            if (type.IsEnum)
            {
                return EnumUsesI64(type) ? "long" : "int";
            }

            if (IsList(type))
            {
                return "list";
            }

            if (IsMap(type))
            {
                return "map";
            }

            return "record";
        }

        private static bool EnumUsesI64(Type type)
        {
            var underlying = Enum.GetUnderlyingType(type);
            return underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(ulong);
        }

        private static bool IsList(Type type)
        {
            if (type.IsArray)
            {
                return true;
            }

            return IsGenericDefinition(type, ListDefinitions);
        }

        private static bool IsMap(Type type)
            => IsGenericDefinition(type, MapDefinitions);

        private static bool IsGenericDefinition(Type type, Type[] definitions)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();
            return Array.IndexOf(definitions, definition) >= 0;
        }
    }
}
