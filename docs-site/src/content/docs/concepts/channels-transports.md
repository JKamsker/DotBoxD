---
title: 'Channels, transports & codecs'
description: 'The communication substrate is deliberately separated from Services and Kernels:'
---
The communication substrate is deliberately separated from Services and Kernels:

> **Channels know nothing about services or kernels. Services and kernels know nothing about named
> pipes or TCP.**

- **Channel** - a transport-neutral duplex byte/frame pipe. The high-performance path is built on
  `System.IO.Pipelines`.
- **Transports** - concrete connection factories:
  - `DotBoxD.Transports.Tcp` - cross-process / network.
  - `DotBoxD.Transports.NamedPipes` - local-machine IPC.
  - an in-process channel is used by tests/benchmarks.
- **Codecs** - wire serialization behind a codec abstraction:
  - `DotBoxD.Codecs.MessagePack` - compact binary with hardened DotBoxD envelope formatters.

All of these target **netstandard2.1**. The current wire does **not** negotiate protocol version,
framing limits, codec, or contracts: both peers must be configured compatibly before connecting. Use
`RpcContractManifest.Fingerprint` in an application-level readiness exchange when deployments need an
explicit contract check.

## Why the stack is transport/codec-neutral (and when to pick each transport)

**The problem it solves.** Wiring a protocol straight to a socket type is the usual trap: the
serialization format, the frame layout, and `if (tcp) … else if (pipe) …` branching leak into the call
path, so moving from local IPC to the network - or from MessagePack to something else - means editing
the code that dispatches calls. DotBoxD avoids that by making the wire and the serializer
constructor-injected strategies. An
[`RpcPeer`](https://github.com/JKamsker/DotBoxD/blob/main/docs/channels/design/peer-model.md) only ever sees two interfaces:
[`IRpcChannel`](https://github.com/JKamsker/DotBoxD/blob/main/src/Services/DotBoxD.Services/Transport/IRpcChannel.cs)
(the pipe) and
[`ISerializer`](https://github.com/JKamsker/DotBoxD/blob/main/src/Services/DotBoxD.Services/Serialization/ISerializer.cs)
(the codec). It never names TCP, named pipes, or MessagePack - that single decision is the whole
mechanism behind the split quoted at the top of this page.

**The payoff.** You can swap the wire (named-pipe → TCP → WebSocket → in-process) and swap the
serialization *without touching a single service contract*. The three ways plugins talk to a host all
ride this one substrate and are equally indifferent to it: [Services / RPC](/concepts/services/) - one C#
contract compiles to a typed proxy + dispatcher, no hand-written marshaling and no runtime reflection on
the hot path; the [query / event pipeline](/concepts/kernels/) - server-side `Where`/`Select` filtering so only
matching, projected values cross the pipe; and [pushdown](/concepts/pushdown/) - moving a loop next to the data
to turn N round-trips into one server-side batch.

Grounded aspects:

- **A channel is just a duplex framed byte pipe - it has no notion of "service" or "method."**
  `IRpcChannel` exposes only `SendAsync`, `ReceiveAsync`, `IsConnected`, and `RemoteEndpoint`; a
  zero-length receive signals "remote closed." Service names, method names, request IDs, and
  cancellation all live *above* this line in the envelope, never in the channel.
- **Framing is shared, not reinvented per transport.** The wire frame -
  `[4B total length][4B messageId][1B MessageType][body]` - is defined once in
  [`MessageFramer`](https://github.com/JKamsker/DotBoxD/blob/main/src/Services/DotBoxD.Services/Protocol/MessageFramer.cs)
  and reused by every stream-backed transport. Named pipes reuse `StreamConnection` directly, so
  named-pipe traffic gets the same length validation, serialized sends, pooled receive buffers, and
  clean EOF behavior as every other stream-backed DotBoxD connection; TCP validates outgoing frames with
  the same `MessageFramer` constants so a malformed frame is rejected locally rather than differing by
  transport.
- **Symmetry falls out of the channel being duplex.** The four core
  [`MessageType`s](https://github.com/JKamsker/DotBoxD/blob/main/src/Services/DotBoxD.Services/Protocol/MessageType.cs)
  (`Request`/`Response`/`Error`/`Cancel`) encode *direction* independently of *who sent the frame*, so
  responses flow back over the same pipe and one read loop can demux both directions. The
  "client = get-only / server = provide-only" asymmetry is peer configuration, not a channel or
  transport property - see [peer-model](https://github.com/JKamsker/DotBoxD/blob/main/docs/channels/design/peer-model.md).
- **Adding a transport = implement three interfaces, change zero contracts.** The
  [WebSocket guide](/channels/websocket-setup/) is proof by construction: it is not a shipped
  package but a walkthrough that implements `ITransport`, `IServerTransport`, and `IRpcChannel` - the
  same `[RpcService]` interfaces and generated proxies then run over a transport the framework never
  shipped.
- **Codec neutrality is the same trick applied to bytes.** `RpcPeer`/`RpcHost` take an `ISerializer`;
  swapping codecs is passing a different one. The
  [MessagePack codec](https://github.com/JKamsker/DotBoxD/blob/main/src/Channels/DotBoxD.Codecs.MessagePack/MessagePackRpcSerializer.cs)
  composes DotBoxD's binary envelope formatters ahead of MessagePack resolvers and enables
  `MessagePackSecurity.UntrustedData`. Its default and `CreateUnityCompatible()` resolver chains include
  reflection-capable standard/contractless fallbacks; they are **not** an IL2CPP guarantee. For AOT,
  generate DTO formatters and pass only the generated/static resolver through `CreateWithResolver`.

### When to reach for each transport

| Transport | Reach for it when | Notes |
|---|---|---|
| **Named pipes** (`DotBoxD.Transports.NamedPipes`) | Same machine, cross-process IPC | Separate package so TCP-only hosts take no pipe dependency; duplex, so one pipe can both serve and call. See [named-pipe transport](/channels/named-pipe-transport/). |
| **TCP** (`DotBoxD.Transports.Tcp`) | Cross-host / over the network | Default [quick-start](/channels/quick-start/) transport; faces untrusted networks, so `TcpConnection` ships a slow-loris defense (`DefaultFrameReadIdleTimeout`, 30s). |
| **WebSocket** (you implement it) | Browser clients, Unity WebGL, or HTTP-based connectivity | Not shipped - implement the three interfaces from the [WebSocket guide](/channels/websocket-setup/). |
| **In-process** (`InMemoryRpcChannel`) | Tests and benchmarks, no OS transport | Shipped in `DotBoxD.Services.Testing`, with `FaultInjectingRpcChannel` for deterministic failures. |

Backpressure and the near-zero-allocation `ValueTask<T>` path are peer options, orthogonal to the
transport you pick - see [performance](/channels/performance/).

For deeper transport material (named pipes, WebSocket extension, performance, design rationale) see the
[Channels (RPC) guide](/channels/quick-start/).

> Roadmap: extracting the transport-neutral abstractions into a dedicated `DotBoxD.Channels` /
> `DotBoxD.Channels.Abstractions` package is tracked in
> [follow-up-issues](https://github.com/JKamsker/DotBoxD/blob/main/docs/architecture/follow-up-issues.md).

## Next step

You have finished the concept layer. To see channels, services, and kernels working together in one
running program, continue to the [GameServer walkthrough](/examples/gameserver-walkthrough/).
