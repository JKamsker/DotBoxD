using System.Reflection;
using System.Text;
using DotBoxD.Kernels.Sandbox;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace DotBoxD.Kernels.Debugging.Clr;

internal static class ClrDebugEvaluationEngine
{
    public static async ValueTask<ClrDebugValue> EvaluateAsync(
        string expression,
        bool allowAwait,
        IReadOnlyDictionary<string, ClrDebugValue> arguments,
        IReadOnlyDictionary<string, ClrDebugValue> locals,
        IReadOnlyDictionary<string, object?> context,
        IReadOnlyCollection<Assembly> references,
        IReadOnlyCollection<string> imports,
        CancellationToken cancellationToken)
    {
        EnsureAwaitAllowed(expression, allowAwait);
        var globals = new ClrDebugScriptGlobals(
            Convert(arguments),
            Convert(locals),
            context);
        var options = CreateOptions(references, imports);
        var source = BuildSource(expression, arguments.Keys.Concat(locals.Keys));
        var result = await CSharpScript.EvaluateAsync<object?>(
                source,
                options,
                globals,
                typeof(ClrDebugScriptGlobals),
                cancellationToken)
            .ConfigureAwait(false);
        return ClrDebugValue.FromClr(result);
    }

    public static IReadOnlyDictionary<string, ClrDebugValue> Snapshot(
        IReadOnlyList<SandboxDebugVariable> variables)
    {
        var values = new Dictionary<string, ClrDebugValue>(variables.Count, StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (variable.IsAssigned)
            {
                values.Add(variable.Name, ClrDebugValue.FromSandbox(variable.Value!));
            }
        }

        return values;
    }

    private static Dictionary<string, object?> Convert(IReadOnlyDictionary<string, ClrDebugValue> values)
        => values.ToDictionary(item => item.Key, item => item.Value.ToClr(), StringComparer.Ordinal);

    private static ScriptOptions CreateOptions(
        IReadOnlyCollection<Assembly> references,
        IReadOnlyCollection<string> imports)
    {
        Assembly[] defaults =
        [
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Task).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,
            typeof(SandboxValue).Assembly,
            typeof(ClrDebugEvaluationEngine).Assembly
        ];
        return ScriptOptions.Default
            .WithReferences(defaults.Concat(references).Distinct())
            .WithImports(
                new[]
                {
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "System.Threading.Tasks"
                }.Concat(imports).Distinct(StringComparer.Ordinal));
    }

    private static string BuildSource(string expression, IEnumerable<string> variableNames)
    {
        var source = new StringBuilder();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in variableNames)
        {
            var keyword = SyntaxFacts.GetKeywordKind(name);
            var contextualKeyword = SyntaxFacts.GetContextualKeywordKind(name);
            if (!declared.Add(name) ||
                (!SyntaxFacts.IsValidIdentifier(name) &&
                 keyword == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None &&
                 contextualKeyword == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None))
            {
                continue;
            }

            var identifier = keyword == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None &&
                contextualKeyword == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
                ? name
                : "@" + name;
            var key = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(name, quote: true);
            source.Append("dynamic ").Append(identifier)
                .Append(" = Locals.ContainsKey(").Append(key).Append(") ? Locals[").Append(key)
                .Append("] : Arguments[").Append(key).AppendLine("];");
        }

        source.Append("return (object?)(").Append(expression).AppendLine(");");
        return source.ToString();
    }

    private static void EnsureAwaitAllowed(string expression, bool allowAwait)
    {
        if (allowAwait)
        {
            return;
        }

        var syntax = SyntaxFactory.ParseExpression(expression);
        if (syntax.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any())
        {
            throw new InvalidOperationException("Await requires an explicit allowAwait request.");
        }
    }
}

public sealed class ClrDebugScriptGlobals
{
    internal ClrDebugScriptGlobals(
        IReadOnlyDictionary<string, object?> arguments,
        IReadOnlyDictionary<string, object?> locals,
        IReadOnlyDictionary<string, object?> context)
    {
        Arguments = arguments;
        Locals = locals;
        Context = context;
    }

    public IReadOnlyDictionary<string, object?> Arguments { get; }

    public IReadOnlyDictionary<string, object?> Locals { get; }

    public IReadOnlyDictionary<string, object?> Context { get; }
}
