using System.Net;
using SafeIR;
using SafeIR.Runtime;

namespace SafeIR.Tests;

internal static class NetworkTestFixtures
{
    public static async ValueTask<SandboxExecutionResult> ExecuteNetworkAsync(string uri, SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("ok"));
        var module = await host.ParseJsonAsync(NetworkJson(uri));
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    public static string NetworkJson(string uri)
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

    public static HttpMessageInvoker FakeInvoker(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? location = null)
        => new(new FakeHttpMessageHandler(response, statusCode, location));

    public static HttpMessageInvoker RedirectFollowedInvoker()
        => new(new RedirectFollowedHandler());

    public static SafeDnsResolver StaticDns(params IPAddress[] addresses)
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

    private sealed class RedirectFollowedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("redirected"),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/config")
            };
            return Task.FromResult(message);
        }
    }
}
