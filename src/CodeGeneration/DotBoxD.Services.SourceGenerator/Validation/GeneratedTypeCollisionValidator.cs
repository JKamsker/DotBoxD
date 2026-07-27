using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class GeneratedTypeCollisionValidator
{
    public static ServiceResult ApplyPrimaryTypes(
        ServiceResult result,
        ExistingTypeIndex existingTypes,
        CancellationToken ct)
    {
        if (result.Model is null || existingTypes.IsEmpty)
        {
            return result;
        }

        var model = result.Model;
        var primaryTypes = GeneratedTypeCollisionKeys.Primary(model);
        var proxy = primaryTypes.Proxy;
        if (existingTypes.Contains(proxy, ct))
        {
            return RejectedService(
                model,
                $"generated proxy type '{proxy.Name}' would collide with an existing type",
                proxy);
        }

        var dispatcher = primaryTypes.Dispatcher;
        if (existingTypes.Contains(dispatcher, ct))
        {
            return RejectedService(
                model,
                $"generated dispatcher type '{dispatcher.Name}' would collide with an existing type",
                dispatcher);
        }

        var extensions = GeneratedTypeCollisionKeys.Extensions;
        if (existingTypes.Contains(extensions, ct))
        {
            return RejectedService(
                model,
                $"generated extension type '{ServicesGeneratorTypeNames.GeneratedNamespace}.{ServicesGeneratorTypeNames.GeneratedExtensionsType}' would collide with an existing type",
                extensions);
        }

        var factory = GeneratedTypeCollisionKeys.Factory;
        if (existingTypes.Contains(factory, ct))
        {
            return RejectedService(
                model,
                $"generated factory type '{ServicesGeneratorTypeNames.GeneratedNamespace}.{ServicesGeneratorTypeNames.GeneratedFactoryType}' would collide with an existing type",
                factory);
        }

        return result;
    }

    public static ServiceResult ApplyAsyncSibling(
        ServiceResult result,
        ExistingTypeIndex existingTypes,
        CancellationToken ct)
    {
        if (result.Model is null ||
            existingTypes.IsEmpty ||
            !NamingHelpers.CanGenerateAsyncSiblingInterface(result.Model.InterfaceName))
        {
            return result;
        }

        var model = result.Model;
        var sibling = GeneratedTypeCollisionKeys.AsyncSibling(model);
        if (!existingTypes.Contains(sibling, ct) || !WillGenerateAsyncSiblingInterface(model, ct))
        {
            return result;
        }

        return RejectedService(
            model,
            $"generated async sibling interface '{sibling.Name}' would collide with an existing type",
            sibling);
    }

    private static bool WillGenerateAsyncSiblingInterface(ServiceModel model, CancellationToken ct)
    {
        if (!NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
        {
            return false;
        }

        var (siblings, _) = AsyncSiblingProjector.Compute(model, ct);
        return !siblings.IsEmpty;
    }

    private static ServiceResult RejectedService(
        ServiceModel model,
        string reason,
        ExistingTypeKey existingType) =>
        new(
            Model: null,
            Error: null,
            MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
            MethodLocations: EquatableArray<DiagnosticLocation>.Empty,
            PropertyLocations: EquatableArray<DiagnosticLocation>.Empty,
            ServiceLocation: default,
            QualifiedInterfaceName: IdentifierHelpers.QualifyTypeName(model.Namespace, model.InterfaceName),
            ServiceDiagnostic: null,
            ExistingTypeCollision: new ExistingTypeCollisionDiagnostic(
                GetDisplayName(model),
                reason,
                existingType));

    private static string GetDisplayName(ServiceModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? IdentifierHelpers.EscapeIdentifier(model.InterfaceName)
            : IdentifierHelpers.EscapeNamespace(model.Namespace) + "." +
                IdentifierHelpers.EscapeIdentifier(model.InterfaceName);
}

internal sealed class PrimaryGeneratedTypeCollisionInputComparer :
    IEqualityComparer<(ServiceResult Left, ExistingTypeIndex Right)>
{
    public static PrimaryGeneratedTypeCollisionInputComparer Instance { get; } = new();

    public bool Equals(
        (ServiceResult Left, ExistingTypeIndex Right) x,
        (ServiceResult Left, ExistingTypeIndex Right) y)
    {
        if (!x.Left.Equals(y.Left))
        {
            return false;
        }

        var model = x.Left.Model;
        if (model is null)
        {
            return true;
        }

        if (!HasSameMembership(x.Right, y.Right, GeneratedTypeCollisionKeys.Extensions) ||
            !HasSameMembership(x.Right, y.Right, GeneratedTypeCollisionKeys.Factory))
        {
            return false;
        }

        var primaryTypes = GeneratedTypeCollisionKeys.Primary(model);
        return HasSameMembership(x.Right, y.Right, primaryTypes.Proxy) &&
            HasSameMembership(x.Right, y.Right, primaryTypes.Dispatcher);
    }

    public int GetHashCode((ServiceResult Left, ExistingTypeIndex Right) obj) =>
        obj.Left.GetHashCode();

    private static bool HasSameMembership(
        ExistingTypeIndex left,
        ExistingTypeIndex right,
        ExistingTypeKey key) =>
        left.Contains(key, CancellationToken.None) == right.Contains(key, CancellationToken.None);
}

internal sealed class AsyncGeneratedTypeCollisionInputComparer :
    IEqualityComparer<(ServiceResult Left, ExistingTypeIndex Right)>
{
    public static AsyncGeneratedTypeCollisionInputComparer Instance { get; } = new();

    public bool Equals(
        (ServiceResult Left, ExistingTypeIndex Right) x,
        (ServiceResult Left, ExistingTypeIndex Right) y)
    {
        if (!x.Left.Equals(y.Left))
        {
            return false;
        }

        var model = x.Left.Model;
        if (model is null || !NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
        {
            return true;
        }

        var sibling = GeneratedTypeCollisionKeys.AsyncSibling(model);
        return x.Right.Contains(sibling, CancellationToken.None) ==
            y.Right.Contains(sibling, CancellationToken.None);
    }

    public int GetHashCode((ServiceResult Left, ExistingTypeIndex Right) obj) =>
        obj.Left.GetHashCode();
}
