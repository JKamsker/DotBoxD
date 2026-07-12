using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcValueGetItemContractTests
{
    public static TheoryData<KernelRpcValue, int, KernelRpcValueKind> ValidIndexes()
        => new()
        {
            { KernelRpcValue.List([KernelRpcValue.Int32(1)]), 0, KernelRpcValueKind.I32 },
            { KernelRpcValue.Record([KernelRpcValue.String("field")]), 0, KernelRpcValueKind.String },
            { KernelRpcValue.Map([KernelRpcValue.String("key"), KernelRpcValue.Bool(true)]), 0, KernelRpcValueKind.String },
            { KernelRpcValue.Map([KernelRpcValue.String("key"), KernelRpcValue.Bool(true)]), 1, KernelRpcValueKind.Bool },
        };

    public static TheoryData<KernelRpcValue, int> InvalidIndexes()
        => new()
        {
            { KernelRpcValue.List([KernelRpcValue.Int32(1)]), -1 },
            { KernelRpcValue.List([KernelRpcValue.Int32(1)]), 1 },
            { KernelRpcValue.Record([KernelRpcValue.Int32(1)]), -1 },
            { KernelRpcValue.Record([KernelRpcValue.Int32(1)]), 1 },
            { KernelRpcValue.Map([KernelRpcValue.String("key"), KernelRpcValue.Int32(1)]), -1 },
            { KernelRpcValue.Map([KernelRpcValue.String("key"), KernelRpcValue.Int32(1)]), 2 },
            { KernelRpcValue.Unit(), 0 },
            { KernelRpcValue.Int32(1), 0 },
        };

    [Theory]
    [MemberData(nameof(ValidIndexes))]
    public void GetItem_returns_items_for_valid_indexes(
        KernelRpcValue value,
        int index,
        KernelRpcValueKind expectedKind)
    {
        Assert.Equal(expectedKind, value.GetItem(index).Kind);
    }

    [Theory]
    [MemberData(nameof(InvalidIndexes))]
    public void GetItem_rejects_invalid_indexes_at_public_boundary(
        KernelRpcValue value,
        int index)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => value.GetItem(index));

        Assert.Equal("index", exception.ParamName);
    }
}
