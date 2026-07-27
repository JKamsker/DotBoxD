using System.Collections.Concurrent;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterValidationCacheTests
{
    [Fact]
    public void Repeated_and_distinct_equal_adapter_shapes_remain_valid()
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: true);
        var cache = new PluginEventAdapterValidationCache();
        var first = new MutableValidationAdapter<GatedCacheEvent>();
        var copiedParameters = new Parameter[] { new("e_Value", SandboxType.I32) };
        var second = new MutableValidationAdapter<GatedCacheEvent>(copiedParameters);

        Assert.Same(first.Parameters, environment.Validate(cache, first));
        Assert.Same(first.Parameters, environment.Validate(cache, first));
        Assert.Same(copiedParameters, environment.Validate(cache, second));
        Assert.Same(copiedParameters, environment.Validate(cache, second));
    }

    [Fact]
    public void Equal_shape_cached_for_an_ungated_interface_does_not_authorize_a_gated_interface()
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: false);
        var cache = new PluginEventAdapterValidationCache();
        var adapter = new DualEventValidationAdapter();

        _ = environment.Validate<UngatedCacheEvent>(cache, adapter);

        var exception = Assert.Throws<SandboxValidationException>(
            () => environment.Validate<GatedCacheEvent>(cache, adapter));
        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("DBXK044", diagnostic.Code);
        Assert.Contains(
            PluginEventAdapterValidationCacheTestFixture.ReadCapability,
            diagnostic.Message,
            StringComparison.Ordinal);

        _ = Assert.Throws<SandboxValidationException>(
            () => environment.Validate<GatedCacheEvent>(cache, adapter));
        Assert.Same(
            adapter.Parameters,
            environment.Validate<UngatedCacheEvent>(cache, adapter));
    }

    [Fact]
    public void In_place_parameter_mutation_is_compared_with_the_cached_copy()
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: true);
        var cache = new PluginEventAdapterValidationCache();
        var parameters = new Parameter[] { new("e_Value", SandboxType.I32) };
        var adapter = new MutableValidationAdapter<GatedCacheEvent>(parameters);
        _ = environment.Validate(cache, adapter);

        parameters[0] = new Parameter("e_Other", SandboxType.I32);
        var exception = Assert.Throws<SandboxValidationException>(() => environment.Validate(cache, adapter));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "DBXK033");

        parameters[0] = new Parameter("e_Value", SandboxType.I32);
        Assert.Same(parameters, environment.Validate(cache, adapter));
    }

    [Theory]
    [InlineData(AdapterShapeMutation.EventName)]
    [InlineData(AdapterShapeMutation.BlankEventName)]
    [InlineData(AdapterShapeMutation.ParameterName)]
    [InlineData(AdapterShapeMutation.BlankParameterName)]
    [InlineData(AdapterShapeMutation.ParameterType)]
    [InlineData(AdapterShapeMutation.ParameterCount)]
    [InlineData(AdapterShapeMutation.NullParameter)]
    [InlineData(AdapterShapeMutation.NullParameters)]
    [InlineData(AdapterShapeMutation.EventValueCount)]
    public void Cached_adapter_shape_mutations_are_revalidated_and_recovery_succeeds(
        AdapterShapeMutation mutation)
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: true);
        var cache = new PluginEventAdapterValidationCache();
        var adapter = new MutableValidationAdapter<GatedCacheEvent>();
        _ = environment.Validate(cache, adapter);

        ApplyMutation(adapter, mutation);
        var exception = Assert.Throws<SandboxValidationException>(() => environment.Validate(cache, adapter));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code is "DBXK031" or "DBXK033" or "DBXK036");

        adapter.EventName = PluginEventAdapterValidationCacheTestFixture.EventName;
        adapter.Parameters = PluginEventAdapterValidationCacheTestFixture.Parameters;
        adapter.EventValueCountOffset = 0;
        Assert.Same(adapter.Parameters, environment.Validate(cache, adapter));
    }

    [Fact]
    public void Concurrent_same_and_equal_adapter_validation_is_safe()
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: true);
        var cache = new PluginEventAdapterValidationCache();
        var adapters = Enumerable.Range(0, 16)
            .Select(_ => new MutableValidationAdapter<GatedCacheEvent>(
                [new Parameter("e_Value", SandboxType.I32)]))
            .ToArray();
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 10_000, i =>
        {
            try
            {
                _ = environment.Validate(cache, adapters[i % adapters.Length]);
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void Concurrent_event_types_on_one_adapter_remain_type_safe()
    {
        var environment = PluginEventAdapterValidationCacheTestFixture.Create(grantReadCapability: false);
        var cache = new PluginEventAdapterValidationCache();
        var adapter = new DualEventValidationAdapter();
        var failures = new ConcurrentQueue<Exception>();
        var gatedRejections = 0;

        Parallel.For(0, 10_000, i =>
        {
            try
            {
                if ((i & 1) == 0)
                {
                    _ = environment.Validate<UngatedCacheEvent>(cache, adapter);
                }
                else
                {
                    var exception = Assert.Throws<SandboxValidationException>(
                        () => environment.Validate<GatedCacheEvent>(cache, adapter));
                    if (exception.Diagnostics.Count != 1 || exception.Diagnostics[0].Code != "DBXK044")
                    {
                        throw exception;
                    }

                    Interlocked.Increment(ref gatedRejections);
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        Assert.Empty(failures);
        Assert.Equal(5_000, gatedRejections);
    }

    private static void ApplyMutation(
        MutableValidationAdapter<GatedCacheEvent> adapter,
        AdapterShapeMutation mutation)
    {
        switch (mutation)
        {
            case AdapterShapeMutation.EventName:
                adapter.EventName = "OtherValidationEvent";
                break;
            case AdapterShapeMutation.BlankEventName:
                adapter.EventName = " ";
                break;
            case AdapterShapeMutation.ParameterName:
                adapter.Parameters = [new Parameter("e_Other", SandboxType.I32)];
                break;
            case AdapterShapeMutation.BlankParameterName:
                adapter.Parameters = [new Parameter(" ", SandboxType.I32)];
                break;
            case AdapterShapeMutation.ParameterType:
                adapter.Parameters = [new Parameter("e_Value", SandboxType.I64)];
                break;
            case AdapterShapeMutation.ParameterCount:
                adapter.Parameters = [];
                break;
            case AdapterShapeMutation.NullParameter:
                adapter.Parameters = new Parameter[] { null! };
                break;
            case AdapterShapeMutation.NullParameters:
                adapter.Parameters = null!;
                break;
            case AdapterShapeMutation.EventValueCount:
                adapter.EventValueCountOffset = 1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    public enum AdapterShapeMutation
    {
        EventName,
        BlankEventName,
        ParameterName,
        BlankParameterName,
        ParameterType,
        ParameterCount,
        NullParameter,
        NullParameters,
        EventValueCount
    }
}
