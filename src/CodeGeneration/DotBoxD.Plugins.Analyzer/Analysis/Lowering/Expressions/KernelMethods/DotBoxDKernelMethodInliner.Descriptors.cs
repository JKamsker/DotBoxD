using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static DotBoxDExpressionModel InlineMetadataDescriptor(
        IMethodSymbol method,
        DotBoxDExpressionLoweringContext context,
        BoundKernelMethodCall call,
        IReadOnlyDictionary<string, DotBoxDExpressionModel> bindings,
        string returnType)
    {
        if (method.IsStatic)
        {
            throw new NotSupportedException($"[KernelMethod] '{method.Name}' must be declared in source.");
        }

        var signature = KernelMethodSignature.Create(method);
        foreach (var attribute in method.ContainingAssembly.GetAttributes())
        {
            if (!DescriptorAttribute(attribute, method, signature, context.SemanticModel.Compilation, out var descriptor))
            {
                continue;
            }

            return InlineDescriptorPayload(method, context, call, bindings, returnType, descriptor);
        }

        throw new NotSupportedException(
            $"Metadata-only context [KernelMethod] '{method.Name}' requires a matching generated descriptor.");
    }

    private static DotBoxDExpressionModel InlineDescriptorPayload(
        IMethodSymbol method,
        DotBoxDExpressionLoweringContext context,
        BoundKernelMethodCall call,
        IReadOnlyDictionary<string, DotBoxDExpressionModel> bindings,
        string returnType,
        KernelMethodDescriptorPayload descriptor)
    {
        ValidateDescriptorHeader(method, returnType, descriptor);
        var occurrences = ValidateDescriptorParameters(method, descriptor);
        ValidateDescriptorArgumentUses(method, occurrences, call, context.SemanticModel, context.CancellationToken);
        var recomputed = RecomputeDescriptorRequirements(method, context, descriptor);
        var shape = RevalidateDescriptorShape(method, context, descriptor);
        var replacements = new List<DescriptorPlaceholderReplacement>();
        var allocates = shape.Allocates;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            var descriptorParameter = descriptor.Parameters[i];
            var expected = DotBoxDTypeNameReader.KernelMethodTypeName(parameter.Type);
            if (!string.Equals(descriptorParameter.Type, expected, StringComparison.Ordinal) ||
                !bindings.TryGetValue(parameter.Name, out var lowered))
            {
                throw new NotSupportedException(
                    $"Generated descriptor for context [KernelMethod] '{method.Name}' has stale parameter metadata.");
            }

            foreach (var occurrence in occurrences)
            {
                if (occurrence.ParameterIndex == i)
                {
                    replacements.Add(new DescriptorPlaceholderReplacement(occurrence.Span, lowered.Source));
                }
            }

            allocates |= lowered.Allocates;
        }

        AddAll(context.Capabilities, recomputed.Capabilities);
        AddAll(context.Effects, recomputed.Effects);
        var source = ReplaceDescriptorPlaceholders(descriptor.Source, replacements);
        return new DotBoxDExpressionModel(source, returnType, allocates);
    }

    private static void ValidateDescriptorHeader(
        IMethodSymbol method,
        string returnType,
        KernelMethodDescriptorPayload descriptor)
    {
        if (DescriptorHeaderMatches(method, returnType, descriptor))
        {
            return;
        }

        throw new NotSupportedException(
            $"Generated descriptor for context [KernelMethod] '{method.Name}' does not match the referenced method.");
    }

    private static bool DescriptorHeaderMatches(
        IMethodSymbol method,
        string returnType,
        KernelMethodDescriptorPayload descriptor)
    {
        if (descriptor.Version != KernelMethodDescriptorPayload.CurrentVersion)
        {
            return false;
        }

        if (!string.Equals(descriptor.ContextType, TypeName(method.ContainingType), StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(descriptor.MethodMetadataName, method.MetadataName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(descriptor.NormalizedSignature, KernelMethodSignature.Create(method), StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(descriptor.ReturnType, returnType, StringComparison.Ordinal))
        {
            return false;
        }

        return descriptor.Parameters.Count == method.Parameters.Length;
    }

    private static bool DescriptorAttribute(
        AttributeData attribute,
        IMethodSymbol method,
        string signature,
        Compilation compilation,
        out KernelMethodDescriptorPayload descriptor)
    {
        descriptor = null!;
        if (!TryReadDescriptorAttribute(attribute, compilation, out var metadata))
        {
            return false;
        }

        if (!DescriptorTargetsMethod(metadata, method, signature))
        {
            return false;
        }

        if (!TryParseDescriptorPayload(metadata, out descriptor))
        {
            throw new NotSupportedException(
                $"Generated descriptor for context [KernelMethod] '{method.Name}' is malformed or has a stale hash.");
        }

        return true;
    }

    private static bool TryReadDescriptorAttribute(
        AttributeData attribute,
        Compilation compilation,
        out DescriptorAttributeMetadata metadata)
    {
        metadata = default;
        if (!IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.GeneratedKernelMethodDescriptorAttribute) ||
            attribute.ConstructorArguments.Length != 6 ||
            attribute.ConstructorArguments[0].Value is not int version ||
            attribute.ConstructorArguments[1].Value is not INamedTypeSymbol contextType ||
            attribute.ConstructorArguments[2].Value is not string methodMetadataName ||
            attribute.ConstructorArguments[3].Value is not string normalizedSignature ||
            attribute.ConstructorArguments[4].Value is not string descriptorHash ||
            attribute.ConstructorArguments[5].Value is not string descriptorPayload)
        {
            return false;
        }

        metadata = new DescriptorAttributeMetadata(
            version,
            contextType,
            methodMetadataName,
            normalizedSignature,
            descriptorHash,
            descriptorPayload);
        return true;
    }

    private static bool DescriptorTargetsMethod(
        DescriptorAttributeMetadata metadata,
        IMethodSymbol method,
        string signature)
        => metadata.Version == KernelMethodDescriptorPayload.CurrentVersion &&
           SymbolEqualityComparer.Default.Equals(metadata.ContextType, method.ContainingType) &&
           string.Equals(metadata.MethodMetadataName, method.MetadataName, StringComparison.Ordinal) &&
           string.Equals(metadata.NormalizedSignature, signature, StringComparison.Ordinal);

    private static bool TryParseDescriptorPayload(
        DescriptorAttributeMetadata metadata,
        out KernelMethodDescriptorPayload descriptor)
    {
        descriptor = null!;
        if (!string.Equals(
                KernelMethodDescriptorPayload.Hash(metadata.DescriptorPayload),
                metadata.DescriptorHash,
                StringComparison.Ordinal) ||
            !KernelMethodDescriptorPayload.TryParse(metadata.DescriptorPayload, out var parsed))
        {
            return false;
        }

        descriptor = parsed!;
        return true;
    }

    private static void AddAll(ICollection<string>? target, IEnumerable<string> values)
    {
        if (target is null)
        {
            return;
        }

        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private readonly record struct DescriptorAttributeMetadata(
        int Version,
        INamedTypeSymbol ContextType,
        string MethodMetadataName,
        string NormalizedSignature,
        string DescriptorHash,
        string DescriptorPayload);

}
