namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static bool SameDescriptorType(DescriptorShape left, DescriptorShape right)
        => string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
           left.Arguments.Count == right.Arguments.Count &&
           left.Arguments.Where((argument, index) => !SameDescriptorType(argument, right.Arguments[index])).Any() == false;

    private static DescriptorShape Unary(
        DescriptorShape[] args,
        string expected,
        string result,
        bool allocates = false)
    {
        if (args.Length != 1 || !string.Equals(args[0].Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale helper argument metadata.");
        }

        return DescriptorShape.Simple(result, allocates || args[0].Allocates);
    }

    private static DescriptorShape Binary(
        DescriptorShape[] args,
        string expected,
        string result,
        bool allocates = false)
    {
        if (args.Length != 2 ||
            !string.Equals(args[0].Type, expected, StringComparison.Ordinal) ||
            !string.Equals(args[1].Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale helper argument metadata.");
        }

        return DescriptorShape.Simple(result, allocates || args.Any(static arg => arg.Allocates));
    }

    private static DescriptorShape NumericUnary(DescriptorShape[] args)
    {
        if (args.Length != 1 || !DotBoxDGenerationNames.ManifestTypes.IsNumeric(args[0].Type))
        {
            throw new NotSupportedException("Generated descriptor contains stale numeric metadata.");
        }

        return args[0];
    }

    private static DescriptorShape NumericBinary(DescriptorShape[] args, bool comparison)
    {
        if (args.Length != 2 ||
            !DotBoxDGenerationNames.ManifestTypes.IsNumeric(args[0].Type) ||
            !string.Equals(args[0].Type, args[1].Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale numeric metadata.");
        }

        return DescriptorShape.Simple(
            comparison ? DotBoxDGenerationNames.ManifestTypes.Bool : args[0].Type,
            args.Any(static arg => arg.Allocates));
    }

    private static DescriptorShape SameTypeBinary(DescriptorShape[] args, string result)
    {
        if (args.Length != 2 || !SameDescriptorType(args[0], args[1]))
        {
            throw new NotSupportedException("Generated descriptor contains stale helper argument metadata.");
        }

        return DescriptorShape.Simple(result, args.Any(static arg => arg.Allocates));
    }

    private static DescriptorShape EqualityBinary(DescriptorShape[] args)
    {
        if (args.Length != 2 ||
            !SameDescriptorType(args[0], args[1]) ||
            !DescriptorEqualityScalar(args[0].Type))
        {
            throw new NotSupportedException("Generated descriptor contains stale equality metadata.");
        }

        return DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.Bool, args.Any(static arg => arg.Allocates));
    }

    private static bool DescriptorEqualityScalar(string type)
        => type is "bool" or "int" or "long" or "double" or "guid";

    private static DescriptorShape StringSubstring(DescriptorShape[] args)
    {
        if (args.Length != 3 ||
            !string.Equals(args[0].Type, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal) ||
            !string.Equals(args[1].Type, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal) ||
            !string.Equals(args[2].Type, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Generated descriptor contains stale string metadata.");
        }

        return DescriptorShape.Simple(DotBoxDGenerationNames.ManifestTypes.String, allocates: true);
    }
}
