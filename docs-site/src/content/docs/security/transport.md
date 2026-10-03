---
title: Transport security and peer identity
description: Authenticate the connection before exposing RPC or plugin installation.
---

`TcpTransport` and `TcpServerTransport` provide plaintext framing. A finite frame timeout limits
stalled reads; it does not provide confidentiality or peer authentication. Plain TCP examples are
for trusted local development. For hostile networks, compose .NET `SslStream` authentication with
DotBoxD's public `StreamConnection`, then create an `RpcPeer` over that channel.

## TLS composition order

1. Establish the socket and wrap its network stream in `SslStream`.
2. Authenticate with `AuthenticateAsClientAsync` or `AuthenticateAsServerAsync`, using a finite
   cancellation deadline. Client `TargetHost` must be the expected server DNS name. Keep certificate
   chain/hostname validation enabled; use OS trust or an explicit custom trust store.
3. For mutual TLS, set `ClientCertificateRequired` and validate against the host's approved client
   trust roots. Inspect `RemoteCertificate` only after authentication. Map its verified identity to
   an application principal and capabilities; a valid certificate alone is not plugin authorization.
4. Optionally exchange `RpcProtocolOffer` values on the authenticated stream, then construct
   `StreamConnection` with the agreed maximum frame size. Start `RpcPeer` only after these steps.
5. Dispose the TLS stream/socket on any authentication or negotiation failure. Do not downgrade to
   plaintext or bypass certificate validation on retry.

Use TLS 1.2/1.3 according to host policy. Production certificate policies should configure revocation
according to their CA and deployment; the ephemeral test CA has no revocation service.

The maintained [TLS composition tests](https://github.com/JKamsker/DotBoxD/blob/main/tests/DotBoxD.Services.Tests/Transport/Security/TlsStreamCompositionTests.cs)
compile and run this sequence, check mutual identity, reject an untrusted certificate and wrong
hostname, and exchange a real frame after negotiation. These are public .NET and DotBoxD primitives;
no special DotBoxD transport or private handshake API is required. Application-specific TLS/identity
policy remains outside the low-level channel.

## Local named pipes

`NamedPipeServerTransport` always creates streams with `PipeOptions.CurrentUserOnly`; there is no
convenience switch that silently makes plugin IPC world-accessible. Windows uses .NET's current-user
pipe security enforcement; Unix uses .NET's current-user enforcement over its local pipe/socket
implementation. Required Linux/Windows transport tests cover same-user connect, cancellation and
lifetime behavior. This is an OS-user boundary, not a distinction between processes owned by that user.

A different-user Windows deployment can hand-create an appropriately ACL-restricted
`NamedPipeServerStream` with the .NET access-control APIs and wrap it in `StreamConnection`.
Unix deployments needing different principals should similarly own the socket/credential policy.
These are deliberate host decisions; never grant all users access just to make an example connect.

Keep kernel capabilities, publisher trust and RPC service authorization separate from transport
identity. A trusted local user or valid TLS peer may still submit untrusted plugin IR.
