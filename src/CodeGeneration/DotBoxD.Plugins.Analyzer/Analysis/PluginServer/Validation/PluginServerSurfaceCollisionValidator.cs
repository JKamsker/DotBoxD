using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerSurfaceCollisionValidator
{
    public static void Validate(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerForwardedProperty> properties,
        IReadOnlyList<PluginServerForwardedMethod> methods,
        IReadOnlyList<PluginServerServiceWrapper> worldServiceWrappers,
        IReadOnlyList<PluginServerControlProperty> controls,
        bool emitsRemoteLocalEventSink)
    {
        var reserved = PluginServerFacadeModelFactory.GeneratedReservedMemberNames();
        PluginServerFacadeModelFactory.AddGeneratedFieldNames(reserved, controls, emitsRemoteLocalEventSink);
        var generatedMembers = new HashSet<string>(reserved, StringComparer.Ordinal);
        PluginServerFacadeModelFactory.AddGeneratedNestedTypeNames(
            generatedMembers,
            worldType,
            worldServiceWrappers,
            controls,
            emitsRemoteLocalEventSink);

        ValidateForwardedPropertyCollisions(reserved, generatedMembers, worldType, properties);
        ValidateForwardedMethodCollisions(reserved, generatedMembers, worldType, methods);
        ValidateControlCollisions(reserved, generatedMembers, worldType, controls);
        PluginServerFacadeModelFactory.ValidateGeneratedSiblingTypeCollisions(serverType, worldType, controls);
        ValidateExistingServerMemberCollisions(serverType, worldType, generatedMembers);
    }

    private static void ValidateForwardedPropertyCollisions(
        HashSet<string> reserved,
        HashSet<string> generatedMembers,
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerForwardedProperty> properties)
    {
        foreach (var property in properties)
        {
            EnsureForwardedWorldMemberAvailable(reserved, generatedMembers, worldType, property.Name);
        }
    }

    private static void ValidateForwardedMethodCollisions(
        HashSet<string> reserved,
        HashSet<string> generatedMembers,
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerForwardedMethod> methods)
    {
        foreach (var methodName in methods
            .Select(static method => method.Name)
            .Distinct(StringComparer.Ordinal))
        {
            EnsureForwardedWorldMemberAvailable(reserved, generatedMembers, worldType, methodName);
        }
    }

    private static void ValidateControlCollisions(
        HashSet<string> reserved,
        HashSet<string> generatedMembers,
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerControlProperty> controls)
    {
        foreach (var control in controls)
        {
            if (reserved.Contains(control.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server control '{control.Name}' collides with the generated facade surface.");
            }

            EnsureSingleFacadeCategory(generatedMembers, worldType, control.Name);
        }
    }

    private static void EnsureForwardedWorldMemberAvailable(
        HashSet<string> reserved,
        HashSet<string> generatedMembers,
        INamedTypeSymbol worldType,
        string name)
    {
        if (reserved.Contains(name))
        {
            throw new NotSupportedException(
                $"Generated plugin server world '{worldType.ToDisplayString()}' member '{name}' collides with the generated facade surface.");
        }

        EnsureSingleFacadeCategory(generatedMembers, worldType, name);
    }

    private static void ValidateExistingServerMemberCollisions(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        HashSet<string> generatedMembers)
    {
        foreach (var member in serverType.GetMembers())
        {
            if (ShouldIgnoreGeneratedFacadeMember(member))
            {
                continue;
            }

            if (member is IMethodSymbol invokeAsync &&
                string.Equals(member.Name, "InvokeAsync", StringComparison.Ordinal))
            {
                EnsureInvokeAsyncDoesNotCollide(serverType, worldType, invokeAsync);
                continue;
            }

            if (generatedMembers.Contains(member.Name))
            {
                ThrowGeneratedServerMemberCollision(serverType, member.Name);
            }
        }
    }

    private static bool ShouldIgnoreGeneratedFacadeMember(ISymbol member)
        => member.IsImplicitlyDeclared ||
           member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ||
           string.Equals(member.Name, "OnConfigured", StringComparison.Ordinal);

    private static void EnsureInvokeAsyncDoesNotCollide(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        IMethodSymbol invokeAsync)
    {
        if (PluginServerFacadeModelFactory.IsGeneratedInvokeAsyncSignature(invokeAsync, worldType))
        {
            ThrowGeneratedServerMemberCollision(serverType, invokeAsync.Name);
        }
    }

    private static void ThrowGeneratedServerMemberCollision(INamedTypeSymbol serverType, string memberName)
        => throw new NotSupportedException(
            $"Generated plugin server '{serverType.ToDisplayString()}' member '{memberName}' collides with the generated facade surface.");

    private static void EnsureSingleFacadeCategory(
        HashSet<string> generatedMembers,
        INamedTypeSymbol worldType,
        string name)
    {
        if (!generatedMembers.Add(name))
        {
            throw new NotSupportedException(
                $"Generated plugin server world '{worldType.ToDisplayString()}' member '{name}' is generated in more than one facade category (forwarded property, method, or control).");
        }
    }
}
