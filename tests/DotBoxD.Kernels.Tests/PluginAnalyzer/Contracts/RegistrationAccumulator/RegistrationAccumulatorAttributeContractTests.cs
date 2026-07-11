namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class RegistrationAccumulatorAttributeContractTests
{
    [Theory]
    [MemberData(nameof(MalformedAccumulatorArguments))]
    public void Registration_accumulator_constructor_rejects_malformed_names(
        string accumulatorName,
        string methodName,
        Type exceptionType,
        string paramName)
    {
        AssertThrowsWithParamName(
            () => new GeneratePluginRegistrationAccumulatorAttribute(accumulatorName, methodName),
            exceptionType,
            paramName);
    }

    [Theory]
    [MemberData(nameof(MalformedRootAccumulatorArguments))]
    public void Registration_root_accumulator_constructor_rejects_malformed_name(
        string accumulatorName,
        Type exceptionType,
        string paramName)
    {
        AssertThrowsWithParamName(
            () => new GeneratePluginRegistrationRootAccumulatorAttribute(accumulatorName),
            exceptionType,
            paramName);
    }

    public static TheoryData<string, string, Type, string> MalformedAccumulatorArguments()
        => new()
        {
            { null!, "RegisterAsync", typeof(ArgumentNullException), "accumulatorName" },
            { "", "RegisterAsync", typeof(ArgumentException), "accumulatorName" },
            { "   ", "RegisterAsync", typeof(ArgumentException), "accumulatorName" },
            { "Registrations", null!, typeof(ArgumentNullException), "methodName" },
            { "Registrations", "", typeof(ArgumentException), "methodName" },
            { "Registrations", "   ", typeof(ArgumentException), "methodName" },
        };

    public static TheoryData<string, Type, string> MalformedRootAccumulatorArguments()
        => new()
        {
            { null!, typeof(ArgumentNullException), "accumulatorName" },
            { "", typeof(ArgumentException), "accumulatorName" },
            { "   ", typeof(ArgumentException), "accumulatorName" },
        };

    private static void AssertThrowsWithParamName(Action construct, Type exceptionType, string paramName)
    {
        var exception = Assert.Throws(exceptionType, construct);

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal(paramName, argumentException.ParamName);
    }
}
