namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class IRExpressionBuilderArgumentContractTests
{
    [Theory]
    [InlineData("ListCountList", "list")]
    [InlineData("RecordGetRecord", "record")]
    [InlineData("StringLengthText", "text")]
    [InlineData("ListGetList", "list")]
    [InlineData("MapGetMap", "map")]
    public void Helper_methods_report_public_parameter_name_for_null_expression_inputs(
        string input,
        string expectedParamName)
    {
        var builder = new IRExpressionBuilder();

        var exception = Assert.ThrowsAny<ArgumentException>(() => Invoke(input, builder));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void Record_reports_fields_parameter_name_for_null_fields_array()
    {
        var builder = new IRExpressionBuilder();

        var exception = Assert.Throws<ArgumentNullException>(() => builder.Record(null!));

        Assert.Equal("fields", exception.ParamName);
    }

    private static void Invoke(string input, IRExpressionBuilder builder)
    {
        _ = input switch
        {
            "ListCountList" => builder.ListCount(null!),
            "RecordGetRecord" => builder.RecordGet(null!, 0),
            "StringLengthText" => builder.StringLength(null!),
            "ListGetList" => builder.ListGet(null!, builder.Int32(0)),
            "MapGetMap" => builder.MapGet(null!, builder.String("key")),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }
}
