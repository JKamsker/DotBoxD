using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using static DotBoxD.Kernels.Tests._TestSupport.NetworkTestFixtures;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpRedirectUriTests
{
    [Fact]
    public void MatchesAllowedAuthority_set_preserves_case_insensitive_string_match()
    {
        var allowedHosts = new HashSet<string>(StringComparer.Ordinal) { "API.EXAMPLE.COM" };

        Assert.True(MatchesAllowedAuthority(allowedHosts, new Uri("https://api.example.com/config")));
    }

    [Theory]
    [InlineData(null, "https://api.example.com/config", false)]
    [InlineData("api.example.com", "https://api.example.com/config", true)]
    [InlineData("API.EXAMPLE.COM", "https://api.example.com/config", true)]
    [InlineData("api.example.com", "https://api.example.com:8443/config", false)]
    [InlineData("api.example.com:8443", "https://api.example.com:8443/config", true)]
    [InlineData("api.example.com:443", "https://api.example.com/config", true)]
    [InlineData("api.example.com:0443", "https://api.example.com/config", true)]
    [InlineData("api.example.com:+443", "https://api.example.com/config", false)]
    [InlineData("api.example.com: 443", "https://api.example.com/config", false)]
    [InlineData("api.example.com:0443", "http://api.example.com:443/config", false)]
    [InlineData("api.example.com:443", "http://api.example.com:443/config", true)]
    [InlineData("api.example.com:80", "http://api.example.com/config", true)]
    [InlineData("api.example.com:080", "http://api.example.com/config", true)]
    [InlineData("api.example.com:80", "https://api.example.com/config", false)]
    [InlineData("api.example.com:080", "https://api.example.com:80/config", false)]
    [InlineData("api.example.com:80", "https://api.example.com:80/config", true)]
    [InlineData("[::1]", "https://[::1]/config", true)]
    [InlineData("[::1]:0443", "https://[::1]/config", true)]
    [InlineData("[::1]:8443", "https://[::1]:8443/config", true)]
    [InlineData("[::1]:08443", "https://[::1]:8443/config", false)]
    [InlineData("[0:0:0:0:0:0:0:1]", "https://[::1]/config", false)]
    [InlineData("127.1", "http://127.1/config", false)]
    [InlineData("127.0.0.1", "http://127.1/config", true)]
    [InlineData("[fe80::1]:443", "https://[fe80::1%25eth0]/config", true)]
    [InlineData("[fe80::1%25eth0]:443", "https://[fe80::1%25eth0]/config", false)]
    [InlineData("bad_host", "https://bad_host/config", true)]
    [InlineData("ftp.example.com", "ftp://ftp.example.com/config", true)]
    [InlineData("ftp.example.com:21", "ftp://ftp.example.com/config", false)]
    [InlineData("api.example.com:443", "custom://api.example.com:443/config", true)]
    [InlineData("api.example.com", "https://api.example.com./config", false)]
    public void Indexed_authorities_preserve_compatibility_matching(
        string? allowedHost,
        string uri,
        bool expected)
    {
        var authorities = allowedHost is null ? [] : new[] { allowedHost };
        var target = new Uri(uri);

        Assert.Equal(expected, SafeHttpUriAudit.MatchesAllowedAuthority(
            new SafeHttpAllowedAuthorityIndex(authorities),
            target));
        Assert.Equal(expected, MatchesAllowedAuthority(
            authorities.ToHashSet(StringComparer.Ordinal),
            target));
    }

    [Fact]
    public async Task Http_get_allows_equal_explicit_port_final_request_uri()
    {
        var host = SandboxTestHost.Create(networkInvoker: new(
            "ok",
            finalRequestUri: "https://api.example.com:8443/config"));
        var module = await host.ImportJsonAsync(NetworkJson("https://api.example.com:8443/config"));
        var policy = NetworkPolicyBuilder()
            .GrantHttpGet(["api.example.com:8443"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();

        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
    }

    private static bool MatchesAllowedAuthority(IReadOnlySet<string> allowedHosts, Uri uri)
        => SafeHttpUriAudit.MatchesAllowedAuthority(allowedHosts, uri);
}
