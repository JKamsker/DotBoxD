using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Security;

public sealed class TlsStreamCompositionTests
{
    [Fact]
    public async Task Mutual_tls_authenticates_peer_identity_before_negotiation_and_rpc()
    {
        using var certificate = CreateCertificate();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        var accepting = listener.AcceptTcpClientAsync(deadline.Token);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        using var server = await accepting;
        await using var serverTls = new SslStream(server.GetStream());
        await using var clientTls = new SslStream(client.GetStream());
        await Task.WhenAll(
            serverTls.AuthenticateAsServerAsync(ServerOptions(certificate), deadline.Token),
            clientTls.AuthenticateAsClientAsync(ClientOptions(certificate, "localhost"), deadline.Token));
        Assert.True(serverTls.IsMutuallyAuthenticated);
        Assert.True(clientTls.IsMutuallyAuthenticated);
        Assert.Equal(certificate.Thumbprint, new X509Certificate2(serverTls.RemoteCertificate!).Thumbprint);
        var offers = await Task.WhenAll(
            RpcProtocolNegotiation.ExchangeAsync(serverTls, new RpcProtocolOffer(), cancellationToken: deadline.Token),
            RpcProtocolNegotiation.ExchangeAsync(clientTls, new RpcProtocolOffer(), cancellationToken: deadline.Token));
        Assert.Equal(offers[0], offers[1]);
        await using var sending = new StreamConnection(clientTls, ownsStream: false, maxMessageSize: offers[0].MaximumFrameSize);
        await using var receiving = new StreamConnection(serverTls, ownsStream: false, maxMessageSize: offers[1].MaximumFrameSize);
        using var frame = MessageFramer.FrameToPayload(7, MessageType.Cancel, []);
        await sending.SendAsync(frame.Memory, deadline.Token);
        using var received = await receiving.ReceiveAsync(deadline.Token);
        Assert.Equal(frame.Memory.ToArray(), received.Memory.ToArray());
    }

    [Theory]
    [InlineData("wrong.example", true)]
    [InlineData("localhost", false)]
    public async Task Unauthenticated_connections_are_rejected(string hostname, bool trustCertificate)
    {
        using var certificate = CreateCertificate();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        var accepting = listener.AcceptTcpClientAsync(deadline.Token);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        using var server = await accepting;
        await using var serverTls = new SslStream(server.GetStream());
        await using var clientTls = new SslStream(client.GetStream());
        var options = ClientOptions(certificate, hostname);
        if (!trustCertificate)
        {
            options.CertificateChainPolicy = null;
        }
        var serverAuthentication = serverTls.AuthenticateAsServerAsync(ServerOptions(certificate), deadline.Token);
        await Assert.ThrowsAsync<AuthenticationException>(() => clientTls.AuthenticateAsClientAsync(options, deadline.Token));
        client.Close();
        try
        {
            await serverAuthentication;
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException)
        {
            // The rejected peer's close may interrupt the other end's TLS handshake.
        }
    }

    private static SslClientAuthenticationOptions ClientOptions(X509Certificate2 certificate, string hostname) => new()
    {
        TargetHost = hostname,
        ClientCertificates = new X509CertificateCollection { certificate },
        CertificateChainPolicy = Trust(certificate),
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
    };

    private static SslServerAuthenticationOptions ServerOptions(X509Certificate2 certificate) => new()
    {
        ServerCertificate = certificate,
        ClientCertificateRequired = true,
        CertificateChainPolicy = Trust(certificate),
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
    };

    private static X509ChainPolicy Trust(X509Certificate2 certificate)
    {
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck // Ephemeral test certificate has no revocation service.
        };
        policy.CustomTrustStore.Add(certificate);
        return policy;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
