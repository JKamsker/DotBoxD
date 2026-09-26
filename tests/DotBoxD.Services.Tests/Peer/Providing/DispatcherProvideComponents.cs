using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Peer.Providing;

internal class ManualProvideDispatcher : IServiceDispatcher
{
    public string ServiceName => "Manual";
    public int Calls { get; private set; }

    public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
        IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default)
    {
        Calls++;
        serializer.Serialize(output, 42);
        return Task.CompletedTask;
    }
}

internal interface IProvideContract;

internal interface IProvideDispatcherContract : IServiceDispatcher;

internal sealed class DualRoleProvideDispatcher : ManualProvideDispatcher,
    IProvideContract, IProvideDispatcherContract
{
    public int FactoryCalls { get; private set; }
    public Exception? FactoryError { get; set; }

    public IServiceDispatcher CreateContractDispatcher()
    {
        FactoryCalls++;
        if (FactoryError is not null)
        {
            throw FactoryError;
        }

        return new ContractDispatcher();
    }

    private sealed class ContractDispatcher : IServiceDispatcher
    {
        public string ServiceName => "Contract";

        public Task DispatchAsync(string method, ReadOnlyMemory<byte> payload, ISerializer serializer,
            IInstanceRegistry registry, IBufferWriter<byte> output, CancellationToken ct = default)
        {
            serializer.Serialize(output, 7);
            return Task.CompletedTask;
        }
    }
}

internal sealed class ProvideServiceProvider(object service) : IServiceProvider
{
    public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
}
