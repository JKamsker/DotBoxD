using System.Net;
using SafeIR;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class SafeNetworkTests
{
    [Fact]
    public async Task Http_get_is_denied_without_host_grant()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Http_get_allows_configured_https_host_and_audits_sanitized_url()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("remote-config"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config?token=secret"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded);
        Assert.Equal("remote-config", ((StringValue)result.Value!).Value);
        Assert.Contains(result.AuditEvents, e =>
            e.BindingId == "net.http.get" &&
            e.ResourceId == "https://api.example.com/config" &&
            e.Success);
    }

    [Fact]
    public async Task Http_get_denies_hosts_outside_allowlist()
    {
        var result = await ExecuteNetworkAsync(
            "https://evil.example.com/config",
            SandboxPolicyBuilder.Create()
                .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_ip_literals_by_default()
    {
        var result = await ExecuteNetworkAsync(
            "https://127.0.0.1/config",
            SandboxPolicyBuilder.Create()
                .GrantHttpGet(["127.0.0.1"], maxResponseBytes: 1024)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_private_ip_literals_unless_explicitly_allowed()
    {
        var result = await ExecuteNetworkAsync(
            "https://192.168.1.20/config",
            SandboxPolicyBuilder.Create()
                .GrantHttpGet(["192.168.1.20"], 1024, allowIpLiterals: true)
                .WithFuel(5_000)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_denies_allowed_hostname_that_resolves_to_private_network()
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("private"),
            dnsResolver: StaticDns(IPAddress.Loopback));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Http_get_allows_private_dns_only_when_explicitly_granted()
    {
        var host = SandboxTestHost.Create(
            networkInvoker: FakeInvoker("private"),
            dnsResolver: StaticDns(IPAddress.Loopback));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], 1024, allowPrivateNetwork: true)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("private", ((StringValue)result.Value!).Value);
    }

    [Fact]
    public async Task Http_get_denies_redirect_responses()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker(
            "",
            HttpStatusCode.Redirect,
            "https://evil.example.com/config"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Direct_policy_negative_http_timeout_is_rejected_at_prepare()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = new SandboxPolicy(
            "bad-http-timeout",
            SandboxEffects.Pure | SandboxEffect.Network,
            [
                new CapabilityGrant(
                    "net.http.get",
                    new Dictionary<string, string> {
                        ["allowedHosts"] = "api.example.com",
                        ["maxResponseBytes"] = "1024",
                        ["timeoutMs"] = "-1"
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxNetworkBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-GRANT-PARAM");
    }

    [Fact]
    public async Task Http_get_enforces_response_byte_limit()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("too-large"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 3)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Deterministic_policy_denies_network_effects()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 1)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-DETERMINISM");
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteNetworkAsync(string uri, SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ParseJsonAsync(NetworkJson(uri));
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string NetworkJson(string uri)
        => $$"""
        {
          "id": "network-reader",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "net.http.get" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "net.http.get",
                    "args": [{ "uri": "{{uri}}" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static HttpMessageInvoker FakeInvoker(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? location = null)
        => new(new FakeHttpMessageHandler(response, statusCode, location));

    private static SafeDnsResolver StaticDns(params IPAddress[] addresses)
        => (_, _) => ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);

    private sealed class FakeHttpMessageHandler(
        string response,
        HttpStatusCode statusCode,
        string? location) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var message = new HttpResponseMessage(statusCode) {
                Content = new StringContent(response)
            };
            if (location is not null) {
                message.Headers.Location = new Uri(location);
            }

            return Task.FromResult(message);
        }
    }
}
