using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using DotBoxD.Shared.HostBindings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static DescriptorShape HostReturnShape(ITypeSymbol type, string bindingId)
    {
        var tag = DotBoxDHostBindingExpressionLowerer.HostBindingReturnTag(type, bindingId);
        if (string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.Unit, StringComparison.Ordinal))
        {
            return DescriptorShape.Simple(tag);
        }

        return DescriptorShapeFromSymbol(DotBoxDTypeNameReader.UnwrapTaskLike(type), tag);
    }

    private static DescriptorShape HostParameterShape(ITypeSymbol type, string bindingId, int index)
    {
        var tag = DotBoxDHostBindingExpressionLowerer.HostBindingManifestTag(type, bindingId, $"argument {index}");
        return DescriptorShapeFromSymbol(type, tag);
    }

    private static DescriptorShape DescriptorShapeFromSymbol(ITypeSymbol type, string tag)
    {
        var source = SandboxTypeSourceEmitter.TryEmit(type);
        if (source is null)
        {
            return DescriptorShape.Simple(tag);
        }

        var expression = SyntaxFactory.ParseExpression(source);
        var shape = SandboxTypeExpressionShape(expression) ?? DescriptorShape.Simple(tag);
        if (!string.Equals(shape.Type, tag, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale host metadata.");
        }

        return shape;
    }

    private static DescriptorShape ListCountShape(
        ObjectCreationExpressionSyntax creation,
        DescriptorShape[] args,
        bool allocates)
    {
        RequireCallGenericNull(creation, "list.count");
        if (args.Length != 1 ||
            !string.Equals(args[0].Type, DotBoxDGenerationNames.ManifestTypes.List, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale list.count metadata.");
        }

        return DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Int, allocates);
    }

    private static DescriptorShape RecordNewShape(ObjectCreationExpressionSyntax creation, DescriptorShape[] args)
    {
        var recordType = CallGenericShape(creation);
        if (recordType is null ||
            !string.Equals(recordType.Type, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal) ||
            recordType.Arguments.Count != args.Length)
        {
            throw new NotSupportedException("Generated descriptor contains stale record.new metadata.");
        }

        for (var i = 0; i < args.Length; i++)
        {
            if (!SameDescriptorType(args[i], recordType.Arguments[i]))
            {
                throw new NotSupportedException("Generated descriptor contains stale record.new metadata.");
            }
        }

        return recordType.WithAllocates(allocates: true);
    }

    private static DescriptorShape RecordGetShape(
        ObjectCreationExpressionSyntax creation,
        DescriptorShape[] args,
        ExpressionSyntax[] argumentExpressions,
        bool allocates)
    {
        if (args.Length != 2 ||
            !string.Equals(args[0].Type, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal) ||
            !string.Equals(args[1].Type, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale record.get metadata.");
        }

        RequireCallGenericNull(creation, "record.get");
        if (ConstantI32(argumentExpressions[1]) is not { } index ||
            index < 0 ||
            index >= args[0].Arguments.Count)
        {
            throw new NotSupportedException("Generated descriptor contains stale record.get metadata.");
        }

        var field = args[0].Arguments[index];
        return field.WithAllocates(allocates);
    }

    private static DescriptorShape? CallGenericShape(ObjectCreationExpressionSyntax creation)
    {
        var arguments = CallConstructorArguments(creation);
        if (arguments.Length < 3)
        {
            return null;
        }

        return SandboxTypeExpressionShape(arguments[2].Expression);
    }

    private static void RequireCallGenericNull(ObjectCreationExpressionSyntax creation, string bindingId)
    {
        var arguments = CallConstructorArguments(creation);
        if (arguments.Length >= 3 &&
            !arguments[2].Expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            throw new NotSupportedException($"Generated descriptor contains stale {bindingId} metadata.");
        }
    }

    private static DescriptorShape? SandboxTypeExpressionShape(ExpressionSyntax expression)
        => expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) => null,
            MemberAccessExpressionSyntax member => SandboxTypeMemberShape(member),
            InvocationExpressionSyntax invocation => SandboxTypeInvocationShape(invocation),
            _ => null
        };

    private static DescriptorShape? SandboxTypeMemberShape(MemberAccessExpressionSyntax member)
        => member.Name.Identifier.ValueText switch
        {
            "Bool" => DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Bool),
            "I32" => DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Int),
            "I64" => DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Long),
            "F64" => DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Double),
            "String" => DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.String),
            "Guid" => DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Guid),
            _ => null
        };

    private static DescriptorShape? SandboxTypeInvocationShape(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            !IsSandboxTypeMember(member))
        {
            return null;
        }

        var args = SandboxTypeArguments(invocation);
        if (args is null)
        {
            return null;
        }

        return member.Name.Identifier.ValueText switch
        {
            "List" => SandboxTypeListShape(args),
            "Map" => SandboxTypeMapShape(args),
            "Record" => SandboxTypeRecordShape(args),
            _ => null
        };
    }

    private static DescriptorShape? SandboxTypeListShape(ExpressionSyntax[] args)
    {
        if (args.Length != 1 || SandboxTypeExpressionShape(args[0]) is not { } item)
        {
            return null;
        }

        return DescriptorShape.Composite(DotBoxDGenerationNames.ManifestTypes.List, [item]);
    }

    private static DescriptorShape? SandboxTypeMapShape(ExpressionSyntax[] args)
    {
        if (args.Length != 2 ||
            SandboxTypeExpressionShape(args[0]) is not { } key ||
            SandboxTypeExpressionShape(args[1]) is not { } value)
        {
            return null;
        }

        return DescriptorShape.Composite(DotBoxDGenerationNames.ManifestTypes.Map, [key, value]);
    }

    private static DescriptorShape? SandboxTypeRecordShape(ExpressionSyntax[] args)
    {
        if (args.Length != 1 || RecordFields(args[0]) is not { } fields)
        {
            return null;
        }

        return DescriptorShape.Composite(DotBoxDGenerationNames.ManifestTypes.Record, fields);
    }

    private static ExpressionSyntax[]? SandboxTypeArguments(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        var expressions = new ExpressionSyntax[args.Count];
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].NameColon is not null ||
                !args[i].RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return null;
            }

            expressions[i] = args[i].Expression;
        }

        return expressions;
    }

    private static DescriptorShape[]? RecordFields(ExpressionSyntax expression)
    {
        if (expression is not ArrayCreationExpressionSyntax { Initializer: { } initializer })
        {
            return null;
        }

        if (initializer.Expressions.Count == 0)
        {
            return null;
        }

        var fields = new DescriptorShape[initializer.Expressions.Count];
        for (var i = 0; i < fields.Length; i++)
        {
            if (SandboxTypeExpressionShape(initializer.Expressions[i]) is not { } field)
            {
                return null;
            }

            fields[i] = field;
        }

        return fields;
    }

    private static int? ConstantI32(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "I32" },
                ArgumentList.Arguments.Count: 1
            } invocation &&
            invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
            literal.Token.Value is int value)
        {
            return value;
        }

        return null;
    }

    private static bool HostReturnAllocates(DescriptorShape shape)
        => HostBindingMetadataRules.ReturnAllocatesManifestTag(shape.Type);

    private sealed record DescriptorShape(string Type, bool Allocates, IReadOnlyList<DescriptorShape> Arguments)
    {
        public static DescriptorShape Simple(string type, bool allocates = false)
            => new(type, allocates, Array.Empty<DescriptorShape>());

        public static DescriptorShape Composite(string type, IReadOnlyList<DescriptorShape> arguments)
            => new(type, Allocates: false, arguments);

        public DescriptorShape WithAllocates(bool allocates) => this with { Allocates = allocates };
    }
}
