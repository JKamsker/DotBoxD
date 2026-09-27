using System.Net;
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
        if (addresses.Count == 0)
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: DNS resolution returned no addresses");
        }

        if (!grant.AllowPrivateNetwork && addresses.Any(SafeIpAddressClassifier.IsNonGlobal))
        {
            throw Error(SandboxErrorCode.PermissionDenied, "net.http.get denied: private network targets are not allowed");
        }

        return addresses;
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
