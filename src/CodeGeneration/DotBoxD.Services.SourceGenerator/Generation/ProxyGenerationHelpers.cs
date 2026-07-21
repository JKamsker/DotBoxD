using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class ProxyGenerationHelpers
{
    public const string CallerCancellationHelperName = "__dotboxd_observeCallerCancellationAsync";
    public const string CallerCancellationCoreHelperName = "__dotboxd_observeCallerCancellationCoreAsync";

    public static bool IsCallerCancellationHelperName(string methodName) =>
        string.Equals(methodName, CallerCancellationHelperName, System.StringComparison.Ordinal) ||
        string.Equals(methodName, CallerCancellationCoreHelperName, System.StringComparison.Ordinal);

    public static void AppendParameterList(
        StringBuilder sb,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct = default)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
                sb.Append(", ");
            var p = parameters[i];
            AppendParameter(sb, p, isLast: i == parameters.Count - 1);
        }
    }

    public static void AppendParameter(StringBuilder sb, ParameterModel p, bool isLast)
    {
        sb.Append(p.CallerInfoAttributePrefix);

        if (p.IsParams && isLast)
        {
            sb.Append("params ");
        }

        sb.Append(p.ScopeKeyword).Append(p.RefKindKeyword).Append(p.Type).Append(' ').Append(p.Name);
        AppendDefaultValue(sb, p);
    }

    public static void AppendAttributes(StringBuilder sb, string attributes)
    {
        if (attributes.Length == 0)
        {
            return;
        }

        var lines = attributes.Split(['\n'], System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            sb.Append("        ").AppendLine(line.TrimEnd('\r'));
        }
    }

    /// <summary>
    /// Appends a parameter's default-value clause to a generated signature: <c>= default</c> for a
    /// cancellation token, the captured literal for any other defaulted parameter, and nothing when
    /// there is no default or it could not be expressed as a literal (preserving the prior behaviour
    /// of silently omitting it rather than emitting invalid code).
    /// </summary>
    public static void AppendDefaultValue(StringBuilder sb, ParameterModel p)
    {
        if (!p.HasDefaultValue)
        {
            return;
        }

        if (p.IsCancellationToken)
        {
            sb.Append(" = default");
        }
        else if (p.DefaultValueLiteral.Length > 0)
        {
            sb.Append(" = ").Append(p.DefaultValueLiteral);
        }
    }

    public static string GetCancellationTokenArgument(
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct = default)
    {
        foreach (var p in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (p.IsCancellationToken)
            {
                return p.Name;
            }
        }

        return "default";
    }

    public static List<ParameterModel> GetRequestParameters(
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct = default)
    {
        var requestParameters = new List<ParameterModel>();
        foreach (var p in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (!p.IsCancellationToken)
            {
                requestParameters.Add(p);
            }
        }

        return requestParameters;
    }

    public static string GetWireType(string type) =>
        type == "dynamic" ? ServicesGeneratorTypeNames.GlobalObject : type;

    public static string GetWireType(ParameterModel parameter) =>
        parameter.StreamKind == ParameterStreamKind.None
            ? GetWireType(parameter.Type)
            : ServicesGeneratorTypeNames.GlobalRpcStreamHandle;

    public static string GetWireArgument(ParameterModel parameter) =>
        parameter.Type == "dynamic"
            ? "(" + ServicesGeneratorTypeNames.GlobalObject + ")" + parameter.Name
            : parameter.Name;

    public static string BuildSubProxyTypeName(string qualifiedInterfaceName)
    {
        const string globalPrefix = ServicesGeneratorTypeNames.GlobalPrefix;
        var startsWithGlobal = qualifiedInterfaceName.StartsWith(globalPrefix, System.StringComparison.Ordinal);
        var searchStart = startsWithGlobal ? globalPrefix.Length : 0;
        var lastDot = qualifiedInterfaceName.LastIndexOf('.');
        var hasNamespace = lastDot >= searchStart;
        var qualifierPart = hasNamespace
            ? qualifiedInterfaceName.Substring(0, lastDot + 1)
            : startsWithGlobal ? globalPrefix : string.Empty;
        var simpleName = hasNamespace
            ? qualifiedInterfaceName.Substring(lastDot + 1)
            : startsWithGlobal ? qualifiedInterfaceName.Substring(globalPrefix.Length) : qualifiedInterfaceName;
        return qualifierPart + NamingHelpers.StripInterfacePrefix(simpleName) + "Proxy";
    }

    public static bool MethodNameRequiresExplicitImplementation(string methodName, string proxyName)
    {
        var unescapedName = IdentifierHelpers.UnescapeIdentifier(methodName);
        return unescapedName == proxyName ||
            unescapedName == "_invoker" ||
            unescapedName == "_instanceId" ||
            unescapedName == "Equals" ||
            unescapedName == "GetHashCode" ||
            unescapedName == "GetType" ||
            unescapedName == "ToString";
    }

    public static string UniqueGeneratedLocalName(
        EquatableArray<ParameterModel> parameters,
        string baseName,
        CancellationToken ct = default)
    {
        var usedNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var parameter in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();
            usedNames.Add(parameter.Name);
        }

        var candidate = baseName;
        var suffix = 1;
        while (usedNames.Contains(candidate))
        {
            ct.ThrowIfCancellationRequested();

            candidate = baseName + suffix;
            suffix++;
        }

        return candidate;
    }
}

internal static class ProxyCallerCancellationHelperEmitter
{
    public static void Append(StringBuilder sb)
    {
        var task = ServicesGeneratorTypeNames.GlobalTask;
        var valueTask = ServicesGeneratorTypeNames.GlobalValueTask;
        var cancellationToken = ServicesGeneratorTypeNames.GlobalCancellationToken;
        var helper = ProxyGenerationHelpers.CallerCancellationHelperName;
        var core = ProxyGenerationHelpers.CallerCancellationCoreHelperName;

        AppendFront(sb, task, helper, core, cancellationToken, generic: false);
        AppendCore(sb, task, core, cancellationToken, generic: false);
        AppendFront(sb, task, helper, core, cancellationToken, generic: true);
        AppendCore(sb, task, core, cancellationToken, generic: true);
        AppendFront(sb, valueTask, helper, core, cancellationToken, generic: false);
        AppendCore(sb, valueTask, core, cancellationToken, generic: false);
        AppendFront(sb, valueTask, helper, core, cancellationToken, generic: true);
        AppendCore(sb, valueTask, core, cancellationToken, generic: true);
    }

    private static void AppendFront(
        StringBuilder sb,
        string taskType,
        string helper,
        string core,
        string cancellationToken,
        bool generic)
    {
        var type = generic ? ServicesGeneratorTypeNames.Generic(taskType, "T") : taskType;
        var typeParameter = generic ? "<T>" : string.Empty;
        sb.AppendLine();
        sb.AppendLine($"        private static {type} {helper}{typeParameter}({type} task, {cancellationToken} ct)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return !ct.CanBeCanceled ? task : {core}(task, ct);");
        sb.AppendLine("        }");
    }

    private static void AppendCore(
        StringBuilder sb,
        string taskType,
        string core,
        string cancellationToken,
        bool generic)
    {
        var type = generic ? ServicesGeneratorTypeNames.Generic(taskType, "T") : taskType;
        var typeParameter = generic ? "<T>" : string.Empty;
        sb.AppendLine();
        sb.AppendLine($"        private static async {type} {core}{typeParameter}({type} task, {cancellationToken} ct)");
        sb.AppendLine("        {");
        if (generic)
        {
            sb.AppendLine("            var result = await task.ConfigureAwait(false);");
            sb.AppendLine("            ct.ThrowIfCancellationRequested();");
            sb.AppendLine("            return result;");
        }
        else
        {
            sb.AppendLine("            await task.ConfigureAwait(false);");
            sb.AppendLine("            ct.ThrowIfCancellationRequested();");
        }

        sb.AppendLine("        }");
    }
}

internal static class ObsoleteAttributeEmitter
{
    public static void AppendIfPresent(StringBuilder sb, string obsoleteAttribute, string indent)
    {
        if (obsoleteAttribute.Length > 0)
        {
            sb.Append(indent).AppendLine(obsoleteAttribute);
        }
    }
}
