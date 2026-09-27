using System.Collections;
using System.Net;
using System.Threading.Tasks.Sources;
using DotBoxD.Hosting.Http.Internal;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

internal sealed class DnsSnapshotFixture
{
    internal static SafeHttpGrantOptions Grant { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https" },
        new SafeHttpAllowedAuthorityIndex(["api.example.com"]), null, 1024,
        TimeSpan.FromSeconds(10), AllowIpLiterals: false, AllowPrivateNetwork: false);

    internal DnsSnapshotFixture(string kind, IPAddress[]? addresses = null)
    {
        var ipv4 = IPAddress.Parse("93.184.216.34");
        Originals = addresses ??
        [
            ipv4,
            IPAddress.Parse("2001:4860:4860::8888%7"),
            IPAddress.Parse("::ffff:93.184.216.34"),
            ipv4,
        ];
        Source = kind switch
        {
            "list" => Originals.ToList(),
            "readonly" => Array.AsReadOnly(Originals),
            "custom" => new ObservedAddresses(Originals),
            _ => Originals,
        };
    }

    internal IPAddress[] Originals { get; }
    internal IReadOnlyList<IPAddress> Source { get; }

    internal void ChangeCollection()
    {
        if (Source is List<IPAddress> list)
        {
            list.Clear();
        }
        else
        {
            Originals[2] = IPAddress.Parse("1.1.1.1");
        }
    }

    internal async Task<IReadOnlyList<IPAddress>> ResolveAsync(string delivery)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<IPAddress>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var custom = new CompletedSource(Source);
        SafeDnsResolver resolver = delivery switch
        {
            "task" => (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(Task.FromResult(Source)),
            "source" => (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(custom, 0),
            "pending" => (_, _) => new ValueTask<IReadOnlyList<IPAddress>>(pending.Task),
            _ => (_, _) => ValueTask.FromResult(Source),
        };
        var operation = SafeHttpDnsResolution.ResolveAsync(Grant, "api.example.com", resolver, CancellationToken.None);
        if (delivery == "pending")
        {
            Assert.False(operation.IsCompleted);
            pending.SetResult(Source);
        }
        else
        {
            Assert.True(operation.IsCompletedSuccessfully);
        }

        var snapshot = await operation;
        Assert.Equal(delivery == "source" ? 1 : 0, custom.Calls);
        return snapshot;
    }

    internal sealed class ObservedAddresses(IPAddress[] addresses, Exception? failure = null) : IReadOnlyList<IPAddress>
    {
        internal int Enumerations { get; private set; }
        public int Count => addresses.Length;
        public IPAddress this[int index] => addresses[index];

        public IEnumerator<IPAddress> GetEnumerator()
        {
            Enumerations++;
            return failure is null
                ? ((IEnumerable<IPAddress>)addresses).GetEnumerator()
                : FailingSequence().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerable<IPAddress> FailingSequence()
        {
            yield return addresses[0];
            throw failure!;
        }
    }

    private sealed class CompletedSource(IReadOnlyList<IPAddress> addresses) : IValueTaskSource<IReadOnlyList<IPAddress>>
    {
        internal int Calls { get; private set; }

        public IReadOnlyList<IPAddress> GetResult(short token)
        {
            Calls++;
            return addresses;
        }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => throw new InvalidOperationException("A completed source does not require a continuation.");
    }
}
