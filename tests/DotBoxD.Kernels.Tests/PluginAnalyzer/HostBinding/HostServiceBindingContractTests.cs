using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceBindingContractTests
{
    [Fact]
    public void AddBindingsFrom_rejects_concrete_service_contracts()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<ConcreteProbeWorld>(new ConcreteProbeWorld()));

        Assert.Contains("must be an interface", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_concrete_service_handles_returned_from_factories()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IConcreteHandleProbeWorld>(new ConcreteHandleProbeWorld()));

        Assert.Contains("must be an interface", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ConcreteProbeHandle), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_overloaded_methods_that_share_a_binding_route()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IOverloadedProbeWorld>(new OverloadedProbeWorld()));

        Assert.Contains("duplicate host binding route", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_duplicate_route_with_same_positional_dto_shape_and_different_field_names()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IDtoShapeProbeWorld>(new DtoShapeProbeWorld()));

        Assert.Contains("duplicate host binding route", ex.Message, StringComparison.Ordinal);
        Assert.Contains("DTO field names", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_duplicate_route_with_same_dto_fields_and_different_clr_types()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IDtoIdentityProbeWorld>(new DtoIdentityProbeWorld()));

        Assert.Contains("duplicate host binding route", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CLR contract", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_duplicate_explicit_property_routes_with_same_return_shape()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IPropertyRouteProbeWorld>(new PropertyRouteProbeWorld()));

        Assert.Contains("duplicate host binding route", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CLR contract", ex.Message, StringComparison.Ordinal);
        Assert.Contains("host.probe.value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_explicit_host_binding_indexer_properties()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IIndexedPropertyProbeWorld>(new IndexedPropertyProbeWorld()));

        Assert.Contains("HostBinding", ex.Message, StringComparison.Ordinal);
        Assert.Contains("indexer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("host.probe.indexed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddBindingsFrom_rejects_explicit_generic_host_binding_methods()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IGenericHostBindingProbeWorld>(new GenericHostBindingProbeWorld()));

        Assert.Contains("generic", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HostBinding", ex.Message, StringComparison.Ordinal);
        Assert.Contains("host.probe.echo", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IGenericHostBindingProbeWorld.Echo), ex.Message, StringComparison.Ordinal);
    }

    private sealed class ConcreteProbeWorld
    {
        [HostBinding("probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        public int Read() => 1;
    }

    private interface IConcreteHandleProbeWorld
    {
        [HostBinding("probe.read.handle", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        ConcreteProbeHandle GetHandle(string id);
    }

    private sealed class ConcreteHandleProbeWorld : IConcreteHandleProbeWorld
    {
        public ConcreteProbeHandle GetHandle(string id) => new();
    }

    private sealed class ConcreteProbeHandle : IConcreteProbeHandle
    {
        public int GetValue() => 1;
    }

    private interface IOverloadedProbeWorld
    {
        [HostBinding("probe.read.text", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(string id);

        [HostBinding("probe.read.number", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(int id);
    }

    private sealed class OverloadedProbeWorld : IOverloadedProbeWorld
    {
        public int Read(string id) => id.Length;

        public int Read(int id) => id;
    }

    private interface IDtoShapeProbeWorld
    {
        [HostBinding("probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(ProbeById probe);

        [HostBinding("probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(ProbeByStats probe);
    }

    private sealed class DtoShapeProbeWorld : IDtoShapeProbeWorld
    {
        public int Read(ProbeById probe) => probe.Id + probe.Score;

        public int Read(ProbeByStats probe) => probe.Level + probe.Rank;
    }

    private sealed record ProbeById(int Id, int Score);

    private sealed record ProbeByStats(int Level, int Rank);

    private interface IDtoIdentityProbeWorld
    {
        [HostBinding("probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(ProbeIdentityA probe);

        [HostBinding("probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(ProbeIdentityB probe);
    }

    private sealed class DtoIdentityProbeWorld : IDtoIdentityProbeWorld
    {
        public int Read(ProbeIdentityA probe) => probe.Id + probe.Score;

        public int Read(ProbeIdentityB probe) => probe.Id + probe.Score;
    }

    private sealed record ProbeIdentityA(int Id, int Score);

    private sealed record ProbeIdentityB(int Id, int Score);

    private interface IPropertyRouteProbeWorld
    {
        [HostBinding(
            "host.probe.value",
            "probe.read.value",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int CurrentValue { get; }

        [HostBinding(
            "host.probe.value",
            "probe.read.value",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int PreviousValue { get; }
    }

    private sealed class PropertyRouteProbeWorld : IPropertyRouteProbeWorld
    {
        public int CurrentValue => 1;

        public int PreviousValue => 2;
    }

    private interface IIndexedPropertyProbeWorld
    {
        [HostBinding(
            "host.probe.indexed",
            "probe.read.indexed",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int this[int id] { get; }
    }

    private sealed class IndexedPropertyProbeWorld : IIndexedPropertyProbeWorld
    {
        public int this[int id] => id;
    }

    private interface IGenericHostBindingProbeWorld
    {
        [HostBinding("host.probe.echo", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Echo<T>(T value);
    }

    private sealed class GenericHostBindingProbeWorld : IGenericHostBindingProbeWorld
    {
        public int Echo<T>(T value) => value?.GetHashCode() ?? 0;
    }
}

[RpcService]
public interface IConcreteProbeHandle
{
    [HostBinding("probe.read.handle.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int GetValue();
}
