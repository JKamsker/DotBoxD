# Channels, transports & codecs

The communication substrate is deliberately separated from Services and Kernels:

> **Channels know nothing about services or kernels. Services and kernels know nothing about named
> pipes or TCP.**

- **Channel** — a transport-neutral duplex byte/frame pipe. The high-performance path is built on
  `System.IO.Pipelines`.
- **Transports** — concrete connection factories:
  - `DotBoxd.Transports.Tcp` — cross-process / network.
  - `DotBoxd.Transports.NamedPipes` — local-machine IPC.
  - an in-process channel is used by tests/benchmarks.
- **Codecs** — wire serialization behind a codec abstraction:
  - `DotBoxd.Codecs.MessagePack` — compact binary, zero-reflection with the generated formatters.

All of these target **netstandard2.1** (Unity/IL2CPP friendly). A connection handshake negotiates
protocol version, framing limits, and codec.

For deeper transport material (named pipes, WebSocket extension, performance, design rationale) see the
legacy RPC docs under [`docs/channels/`](../channels/).

> Roadmap: extracting the transport-neutral abstractions into a dedicated `DotBoxd.Channels` /
> `DotBoxd.Channels.Abstractions` package is tracked in
> [follow-up-issues](../architecture/follow-up-issues.md).
