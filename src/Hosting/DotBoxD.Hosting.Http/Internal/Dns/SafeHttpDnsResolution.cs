using System.Net;
using System.Net.Sockets;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Http.Internal;

internal static class SafeHttpDnsResolution
{
    internal static async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        SafeHttpGrantOptions grant,
        string host,
        SafeDnsResolver? dnsResolver,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            RequireIpLiteralAllowed(grant, address);
            return [address];
        }

        var resolution = (dnsResolver ?? ResolveDefaultAsync)(host, cancellationToken);
        IReadOnlyList<IPAddress> addresses;
        if (resolution.IsCompletedSuccessfully)
        {
            addresses = resolution.Result;
        }
        else
        {
            var pending = resolution.AsTask();
            try
            {
                addresses = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObserveLateFailure(pending);
                throw;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = SnapshotAddresses(addresses);
        if (snapshot.Length == 0)
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: DNS resolution returned no addresses");
        }

        if (!grant.AllowPrivateNetwork && snapshot.Any(SafeIpAddressClassifier.IsNonGlobal))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }

        return snapshot;
    }

    private static IPAddress[] SnapshotAddresses(IReadOnlyList<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var snapshot = addresses.ToArray();
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < snapshot.Length; index++)
        {
            var address = snapshot[index];
            ArgumentNullException.ThrowIfNull(address);
            if (!address.TryWriteBytes(bytes, out var length))
            {
                throw new InvalidOperationException("DNS address could not be copied");
            }

            // IPAddress values are mutable too; pin only the values that this request validates.
            snapshot[index] = address.AddressFamily == AddressFamily.InterNetworkV6
                ? new IPAddress(bytes[..length], address.ScopeId)
                : new IPAddress(bytes[..length]);
        }
        return snapshot;
    }

    private static void ObserveLateFailure(Task pending)
    {
        if (pending.IsCompleted)
        {
            _ = pending.Exception;
            return;
        }

        // The resolver can finish after cancellation has detached the request's waiter.
        _ = pending.ContinueWith(
            static completed => { _ = completed.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void RequireIpLiteralAllowed(SafeHttpGrantOptions grant, IPAddress address)
    {
        if (!grant.AllowIpLiterals)
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: IP literals are not allowed");
        }

        if (!grant.AllowPrivateNetwork && SafeIpAddressClassifier.IsNonGlobal(address))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }
    }

    private static async ValueTask<IReadOnlyList<IPAddress>> ResolveDefaultAsync(string host, CancellationToken cancellationToken)
        => await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message) => new(new SandboxError(code, message));
}
