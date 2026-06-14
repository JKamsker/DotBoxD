# DotBoxd documentation

DotBoxd is a source-generated, contract-first .NET extension runtime. One C# contract can be used in
three ways:

- **[Services](concepts/services.md)** — the host implements a contract; clients call it remotely (RPC).
- **[Kernels](concepts/kernels.md)** — a client supplies validated logic the host runs safely inside a
  metered sandbox (restricted IR — never C#/IL/reflection).
- **[Pushdown](concepts/pushdown.md)** — a kernel composes the host's own services server-side, so many
  small remote calls collapse into one validated round-trip.

```mermaid
flowchart LR
    Client["Client / Plugin"] -->|remote call| Services
    Client -->|submit validated IR| Kernels
    Client -->|one submission| Pushdown
    Services --> Host["Host process"]
    Kernels --> Host
    Pushdown --> Kernels
    Pushdown --> Services
```

## Map

- **Getting started** — [`getting-started/`](getting-started/): install, first service, first kernel,
  pushdown quickstart.
- **Concepts** — [`concepts/`](concepts/): services, kernels, pushdown, channels & transports, the
  kernel runtime (interpreted vs verified-IL, fuel/quotas/capabilities).
- **Security** — [`security/`](security/): the threat model and the all-important
  [sandbox caveats](security/sandbox-caveats.md) (what is and isn't a boundary). See also the top-level
  [`SECURITY.md`](../SECURITY.md).
- **Reference** — [`reference/diagnostics.md`](reference/diagnostics.md) (DBXS/DBXK codes),
  [`reference/schemas.md`](reference/schemas.md) (kernel/plugin JSON schemas).
- **Specifications** — [`Specs/`](Specs/): the full kernel sandbox spec (IR language, type system,
  effects/capabilities, threat model, runtime).
- **Contributing** — [`contributing/migration-from-standalone-repos.md`](contributing/migration-from-standalone-repos.md):
  how this repo merges the former ShaRPC + Safe-IR projects and how to view their pre-merge history.
- **Channels (legacy RPC docs)** — [`channels/`](channels/): quick-start, API reference, Unity
  integration, named-pipe/websocket transports, performance, design notes.

## Runnable example

The acceptance sample [`samples/Pushdown/DotBoxd.EndToEnd`](../samples/Pushdown/DotBoxd.EndToEnd)
demonstrates all three modes end to end:

```bash
dotnet run -c Release --project samples/Pushdown/DotBoxd.EndToEnd
```
