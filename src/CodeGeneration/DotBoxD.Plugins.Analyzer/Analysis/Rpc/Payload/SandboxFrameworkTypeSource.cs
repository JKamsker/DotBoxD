using Microsoft.CodeAnalysis;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class SandboxFrameworkTypeSource
{
    private const int MaxDepth = 8;
    private const string SandboxType = TypeNames.GlobalSandboxType;
    private static readonly FrameworkManifestTag[] ManifestTags =
    [
        new(DotBoxDRpcTypeMapper.IsGuid, ManifestTypes.Guid),
        new(DotBoxDRpcTypeMapper.IsDateTimeWireType, ManifestTypes.Record),
        new(DotBoxDRpcTypeMapper.IsDecimalWireType, ManifestTypes.Record),
        new(DotBoxDRpcTypeMapper.IsDateOnlyWireType, ManifestTypes.Int),
        new(DotBoxDRpcTypeMapper.IsTimeOnlyWireType, ManifestTypes.Long),
        new(DotBoxDRpcTypeMapper.IsTimeSpanWireType, ManifestTypes.Long),
        new(DotBoxDRpcTypeMapper.IsCancellationTokenWireType, ManifestTypes.Bool),
        new(DotBoxDRpcTypeMapper.IsIndexWireType, ManifestTypes.Record),
        new(DotBoxDRpcTypeMapper.IsRangeWireType, ManifestTypes.Record),
    ];

    private static readonly FrameworkSandboxSource[] SandboxSources =
    [
        new(DotBoxDRpcTypeMapper.IsGuid, _ => SandboxType + ".Guid"),
        new(DotBoxDRpcTypeMapper.IsDateTimeWireType, DateTimeSandboxSource),
        new(DotBoxDRpcTypeMapper.IsDecimalWireType, DecimalSandboxSource),
        new(DotBoxDRpcTypeMapper.IsDateOnlyWireType, _ => SandboxType + ".I32"),
        new(DotBoxDRpcTypeMapper.IsTimeOnlyWireType, _ => SandboxType + ".I64"),
        new(DotBoxDRpcTypeMapper.IsTimeSpanWireType, _ => SandboxType + ".I64"),
        new(DotBoxDRpcTypeMapper.IsCancellationTokenWireType, _ => SandboxType + ".Bool"),
        new(DotBoxDRpcTypeMapper.IsIndexWireType, IndexSandboxSource),
        new(DotBoxDRpcTypeMapper.IsRangeWireType, RangeSandboxSource),
    ];

    public static string? ManifestTag(ITypeSymbol type)
    {
        foreach (var candidate in ManifestTags)
        {
            if (candidate.Matches(type))
            {
                return candidate.Tag;
            }
        }

        return null;
    }

    public static string? SandboxSource(ITypeSymbol type, int depth)
    {
        foreach (var candidate in SandboxSources)
        {
            if (candidate.Matches(type))
            {
                return candidate.Source(depth);
            }
        }

        return null;
    }

    private static string DateTimeSandboxSource(int depth)
    {
        RejectNestedRecordAtDepth(depth);
        return $"{SandboxType}.Record(new {SandboxType}[] {{ {SandboxType}.I64, {SandboxType}.I64 }})";
    }

    private static string DecimalSandboxSource(int depth)
    {
        RejectNestedRecordAtDepth(depth);
        return $"{SandboxType}.Record(new {SandboxType}[] {{ {SandboxType}.I32, {SandboxType}.I32, {SandboxType}.I32, {SandboxType}.I32 }})";
    }

    private static string IndexSandboxSource(int depth)
    {
        RejectNestedRecordAtDepth(depth);
        return $"{SandboxType}.Record(new {SandboxType}[] {{ {SandboxType}.I32, {SandboxType}.Bool }})";
    }

    private static string RangeSandboxSource(int depth)
    {
        RejectRangeRecordAtDepth(depth);
        var indexType = $"{SandboxType}.Record(new {SandboxType}[] {{ {SandboxType}.I32, {SandboxType}.Bool }})";
        return $"{SandboxType}.Record(new {SandboxType}[] {{ {indexType}, {indexType} }})";
    }

    private static void RejectNestedRecordAtDepth(int depth)
    {
        if (depth >= MaxDepth)
        {
            throw new NotSupportedException();
        }
    }

    private static void RejectRangeRecordAtDepth(int depth)
    {
        if (depth + 1 >= MaxDepth)
        {
            throw new NotSupportedException();
        }
    }

    private readonly record struct FrameworkManifestTag(Func<ITypeSymbol, bool> Matches, string Tag);

    private readonly record struct FrameworkSandboxSource(Func<ITypeSymbol, bool> Matches, Func<int, string> Source);
}
