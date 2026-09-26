---
title: WebSocket transport status
description: WebSocket is an application-owned extension point, not a shipped DotBoxD transport.
---

DotBoxD does **not** ship `DotBoxD.Transports.WebSocket`, `WebSocketTransport`, or
`WebSocketConnection`. The previous copy-and-paste walkthrough was unverified and has been removed.
There is no maintained WebGL/WebSocket implementation or compatibility claim in this release.

Applications can implement the public `IRpcChannel` contract using their WebSocket stack.
`ReceiveAsync(CancellationToken)` returns `Task<Payload>`; callers own and dispose each received
payload, and an empty payload signals connection closure. `SendAsync` accepts one complete framed
message. An adapter must handle fragmented WebSocket messages, bound accumulated frame size, serialize
sends, honor cancellation/disposal, validate frames and transfer buffer ownership correctly.

For browser deployments, authenticate the HTTP upgrade, validate allowed origins and use WSS.
Cookie/token authorization belongs to the host. Do not reuse the TCP plaintext examples as a security
configuration. See [transport security](/security/transport/) and the
[public channel contract](https://github.com/JKamsker/DotBoxD/blob/main/src/Services/DotBoxD.Services/Transport/IRpcChannel.cs).

Use the shipped [named-pipe transport](/channels/named-pipe-transport/) for local sidecars or TCP
with authenticated stream composition for native clients. A custom adapter should run the consumer
testing kit's cancellation, closure, malformed-frame and ownership checks before deployment.
