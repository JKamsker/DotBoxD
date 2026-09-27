using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Policies;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Policy;

internal static class ParameterReaderAllocationFixture
{
    internal static void AssertAllocation(ITestOutputHelper output, string name, object parameters, Func<string> format)
    {
        var grant = Assert.Single(SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build().Grants);
        Assert.Equal(format(), grant.Parameters["Value"]);
        var textParameters = new PolicyParameterValue<string>(string.Empty);
        _ = Measure(parameters, textParameters, format, useText: false);
        _ = Measure(parameters, textParameters, format, useText: true);
        var valueBytes = Measure(parameters, textParameters, format, useText: false);
        var textBytes = Measure(parameters, textParameters, format, useText: true);
        output.WriteLine($"{name}: value {valueBytes / 1000D} B/grant; text control {textBytes / 1000D} B/grant");
        Assert.True(valueBytes <= textBytes, $"Value conversion allocated {valueBytes - textBytes} extra bytes.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(object parameters, PolicyParameterValue<string> textParameters, Func<string> format, bool useText)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            if (useText)
            {
                textParameters.Value = format();
            }

            var policy = SandboxPolicyBuilder.Create().Grant("custom.parameters", useText ? textParameters : parameters).Build();
            GC.KeepAlive(policy);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}

internal sealed class PolicyParameterValue<T>(T value)
{
    public T Value { get; set; } = value;
}
