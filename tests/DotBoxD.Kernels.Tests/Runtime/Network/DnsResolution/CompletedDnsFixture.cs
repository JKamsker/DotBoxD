using System.Net;
using System.Threading.Tasks.Sources;
using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

internal sealed class CompletedDnsFixture : IDisposable
{
    internal static readonly IReadOnlyList<IPAddress> Addresses = [IPAddress.Parse("93.184.216.34")];
    private readonly SandboxPolicy _policy = SandboxPolicyBuilder.Create()
        .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
        .WithFuel(5_000)
        .WithWallTime(TimeSpan.FromSeconds(10))
        .Build();
    private readonly BindingRegistry _registry = new BindingRegistryBuilder().Build();
    private readonly SafeInMemoryHttpMessageInvoker _invoker = new("ok");
    private readonly SandboxUri _uri = new("https://api.example.com/config");

    internal (SandboxContext Context, InMemoryAuditSink Audit) CreateScenario()
    {
        var audit = new InMemoryAuditSink();
        return (new SandboxContext(SandboxRunId.New(), _policy, new ResourceMeter(_policy.ResourceLimits),
            _registry, audit, CancellationToken.None), audit);
    }

    internal ValueTask<string> Start(SandboxContext context, SafeDnsResolver resolver, CancellationToken ct = default) =>
        SafeHttpClient.GetTextAsync(context, _uri, _invoker, resolver, ct);

    public void Dispose() => _invoker.Dispose();

    internal sealed class Source : IValueTaskSource<IReadOnlyList<IPAddress>>
    {
        private ManualResetValueTaskSourceCore<IReadOnlyList<IPAddress>> _core = new()
        {
            RunContinuationsAsynchronously = true,
        };
        private int _resultCalls;
        internal int ResultCalls => Volatile.Read(ref _resultCalls);
        internal TaskCompletionSource Consumed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ValueTask<IReadOnlyList<IPAddress>> Result => new(this, _core.Version);

        internal void Complete(Exception? error = null)
        {
            if (error is null)
            {
                _core.SetResult(Addresses);
            }
            else
            {
                _core.SetException(error);
            }
        }

        public IReadOnlyList<IPAddress> GetResult(short token)
        {
            if (Interlocked.Increment(ref _resultCalls) != 1)
            {
                throw new InvalidOperationException("DNS result was consumed more than once.");
            }
            try
            {
                return _core.GetResult(token);
            }
            finally
            {
                Consumed.TrySetResult();
            }
        }

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}
