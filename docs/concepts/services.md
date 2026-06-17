# Services (RPC)

A **Service** is a handwritten host capability behind a shared C# contract. Annotate an interface with
`[DotBoxDService]` and the `DotBoxD.Services.SourceGenerator` emits, at compile time:

- a typed **client proxy** (calls marshal over the wire, no runtime reflection),
- a server **dispatcher**, and
- `Provide{Service}` / `Get<TService>()` wiring extensions.

The runtime is **peer-based and bidirectional** (`RpcPeer` / `RpcHost`): one connection can both serve
and call services. It is transport- and codec-neutral:

- **Transports**: `DotBoxD.Transports.Tcp`, `DotBoxD.Transports.NamedPipes` (and an in-process channel
  for tests). Channels carry framed messages and know nothing about services.
- **Codecs**: `DotBoxD.Codecs.MessagePack`.

The Services + channel + codec libraries target **netstandard2.1**, so they run on **Unity / IL2CPP**.

Diagnostics from the generator use the `DBXS###` prefix — see
[reference/diagnostics.md](../reference/diagnostics.md).

**See also:** the GameServer sample under
[`samples/GameServer`](../../samples/GameServer), the legacy RPC docs under
[`docs/channels/`](../channels/) (quick-start, API reference, Unity integration, transports,
performance), [pushdown](pushdown.md) for composing services server-side, and
[`docs/examples/coverage-gaps.md`](../examples/coverage-gaps.md) for service scenarios no longer shown
by maintained samples.
