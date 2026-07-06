using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static PluginServerForwardedMethod[] ResolveMethods(
        INamedTypeSymbol controlType,
        Dictionary<string, ServiceWrapperBuilder> serviceWrappers,
        CancellationToken cancellationToken)
    {
        var methods = new List<PluginServerForwardedMethod>();
        var seenMethods = new Dictionary<string, SeenForwardedMethod>(StringComparer.Ordinal);
        foreach (var member in MembersIncludingInherited(controlType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.ContainingType is not null &&
                IsControlPlaneMember(member.ContainingType))
            {
                continue;
            }

            if (member is IEventSymbol eventSymbol)
            {
                throw new NotSupportedException(
                    $"Generated plugin server member '{eventSymbol.ToDisplayString()}' is an event; events are not supported on generated plugin server facades.");
            }

            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                ValidateForwardedMethod(method);
                var (returnWrapperName, returnWrapperKind) = ResolveReturnWrapper(
                    method.ReturnType,
                    serviceWrappers,
                    cancellationToken);
                var forwarded = new PluginServerForwardedMethod(
                    method.Name,
                    TypeName(method.ContainingType),
                    TypeName(method.ReturnType),
                    PluginServerFlowAttributeSource.MemberAttributes(method),
                    PluginServerFlowAttributeSource.ReturnAttributes(method),
                    PluginServerXmlDocumentation.FromSymbol(
                        method,
                        "Forwards " + method.Name + " to the remote domain service.",
                        cancellationToken),
                    returnWrapperName,
                    returnWrapperKind,
                    new EquatableArray<PluginServerParameter>(ResolveParameters(method)));
                var signature = PluginServerForwardedMethodSignature.Key(method);
                if (seenMethods.TryGetValue(signature, out var existing))
                {
                    if (!PluginServerForwardedMethodSignature.HasSameTupleElementNames(
                        existing.Symbol,
                        method,
                        cancellationToken))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with incompatible tuple element names.");
                    }

                    if (!string.Equals(existing.Method.ReturnType, forwarded.ReturnType, StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with a different return type.");
                    }

                    if (!existing.Method.Attributes.Equals(forwarded.Attributes))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with different member attributes.");
                    }

                    if (!existing.Method.ReturnAttributes.Equals(forwarded.ReturnAttributes))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with different return-flow attributes.");
                    }

                    if (!HasSameParameterDefaultShape(existing.Symbol, method))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited optional/default parameter conflict.");
                    }

                    if (!HasSameCallerInfoAttributes(existing.Symbol, method, cancellationToken))
                    {
                        throw new NotSupportedException(
                            $"Generated plugin server member '{method.ToDisplayString()}' has an inherited signature collision with incompatible caller info attributes.");
                    }

                    if (IsMoreDerivedMember(method, existing.Symbol))
                    {
                        seenMethods[signature] = new SeenForwardedMethod(method, forwarded, existing.Index);
                        methods[existing.Index] = forwarded;
                    }

                    continue;
                }

                seenMethods.Add(signature, new SeenForwardedMethod(method, forwarded, methods.Count));
                methods.Add(forwarded);
            }
        }

        return methods.ToArray();
    }

    private static void ValidateForwardedMethod(IMethodSymbol method)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' is a non-public interface method '{method.Name}'; generated plugin server facades may forward public members only.");
        }

        if (method.IsStatic)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must be an instance method.");
        }

        if (method.IsGenericMethod)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must not be generic.");
        }

        if (method.RefKind != RefKind.None)
        {
            throw new NotSupportedException(
                $"Generated plugin server member '{method.ToDisplayString()}' must not declare ref returns.");
        }

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"Generated plugin server member '{method.ToDisplayString()}' must not declare ref, out, or in parameters.");
            }
        }
    }

    private static bool HasSameParameterDefaultShape(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            var leftPreservesMetadata =
                ShouldPreserveMetadataDefaultAttributes(left, i, out var leftDefaultClause);
            var rightPreservesMetadata =
                ShouldPreserveMetadataDefaultAttributes(right, i, out var rightDefaultClause);
            if (!string.Equals(leftDefaultClause, rightDefaultClause, StringComparison.Ordinal) ||
                leftPreservesMetadata != rightPreservesMetadata)
            {
                return false;
            }

            if (leftPreservesMetadata &&
                !string.Equals(
                    ParameterDefaultValueEmitter.FormatMetadataDefaultAttributePrefix(left.Parameters[i], true),
                    ParameterDefaultValueEmitter.FormatMetadataDefaultAttributePrefix(right.Parameters[i], true),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMoreDerivedMember(IMethodSymbol candidate, IMethodSymbol existing)
        => !SymbolEqualityComparer.Default.Equals(candidate.ContainingType, existing.ContainingType) &&
           candidate.ContainingType.AllInterfaces.Any(
               interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, existing.ContainingType));

    private static bool HasSameCallerInfoAttributes(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken cancellationToken)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (CallerInfoKey(left.Parameters[i], cancellationToken) !=
                CallerInfoKey(right.Parameters[i], cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static string CallerInfoKey(IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        var attributes = new List<string>();
        foreach (var attribute in parameter.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.Runtime.CompilerServices.CallerMemberNameAttribute":
                    attributes.Add("member");
                    break;

                case "System.Runtime.CompilerServices.CallerFilePathAttribute":
                    attributes.Add("file");
                    break;

                case "System.Runtime.CompilerServices.CallerLineNumberAttribute":
                    attributes.Add("line");
                    break;

                case "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute":
                    attributes.Add("argument:" + CallerArgumentExpressionTarget(attribute));
                    break;
            }
        }

        attributes.Sort(StringComparer.Ordinal);
        return string.Join("|", attributes);
    }

    private static string CallerArgumentExpressionTarget(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is string target)
        {
            return target;
        }

        return string.Empty;
    }

    private readonly record struct SeenForwardedMethod(
        IMethodSymbol Symbol,
        PluginServerForwardedMethod Method,
        int Index);
}
