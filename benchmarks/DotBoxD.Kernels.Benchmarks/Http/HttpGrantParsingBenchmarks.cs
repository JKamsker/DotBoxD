using DotBoxD.Hosting.Http.Bindings;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Http;

using System.Net;
using BenchmarkDotNet.Attributes;
using DotBoxD.Hosting.Http;

[MemoryDiagnoser]
public class HttpGrantParsingBenchmarks
{
    private BindingRegistry _bindings = null!;
    private SandboxContext _context = null!;
    private SandboxPolicy _policy = null!;
    private SafeInMemoryHttpMessageInvoker _invoker = null!;
    private readonly SandboxUri _uri = new("https://api.example.com/config");

    [Params(0, 32, 1024, 65536)]
    public int ResponseBytes { get; set; }

    [Params(1, 10, 1_000)]
    public int RequestCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var responseBytesPerIteration = checked((long)ResponseBytes * RequestCount);
        _invoker = new SafeInMemoryHttpMessageInvoker(new byte[ResponseBytes]);
        _bindings = new BindingRegistryBuilder()
            .AddNetworkBindings(_invoker, StaticDns)
            .Build();
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1_000_000)
            .WithFuel(checked(responseBytesPerIteration + 10_000_000))
            .WithMaxAllocatedBytes(checked((responseBytesPerIteration * 3) + 10_000_000))
            .WithMaxTotalStringBytes(checked((responseBytesPerIteration * 2) + 1_048_576))
            .WithWallTime(TimeSpan.FromSeconds(30))
            .Build();
        _policy = policy with
        {
            ResourceLimits = policy.ResourceLimits with
            {
                MaxNetworkBytesRead = checked(responseBytesPerIteration + 1_048_576)
            }
        };
    }

    [IterationSetup]
    public void ResetContext()
        => _context = new SandboxContext(
            SandboxRunId.New(),
            _policy,
            new ResourceMeter(_policy.ResourceLimits),
            _bindings,
            new InMemoryAuditSink(),
            CancellationToken.None);

    [Benchmark]
    public async ValueTask RepeatedHttpGets()
    {
        for (var i = 0; i < RequestCount; i++)
        {
            _ = await SafeHttpClient.GetTextAsync(_context, _uri, _invoker, StaticDns, CancellationToken.None);
        }
    }

    private static ValueTask<IReadOnlyList<IPAddress>> StaticDns(string host, CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
}
