using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryConstantOperandCancellationTests
{
    [Fact]
    public void TranslateFilter_preserves_cancellation_from_captured_scalar_getter()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var probe = new CancelingConstantProbe(cts.Token);

        var ex = Assert.Throws<OperationCanceledException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage == probe.Value));

        Assert.Equal("constant getter canceled", ex.Message);
        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    private sealed class CancelingConstantProbe(CancellationToken cancellationToken)
    {
        public int Value => throw new OperationCanceledException("constant getter canceled", cancellationToken);
    }
}
