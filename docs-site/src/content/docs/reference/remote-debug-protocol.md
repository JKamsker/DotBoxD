---
title: Remote debug protocol v1
description: Frozen generic duplex wire contract used by the DotBoxD kernel debug bridge.
---

Protocol version 1 is the compatibility baseline for remote kernel debugging. It is transported through two
application-independent RPC services:

```csharp
ValueTask<byte[]> IPluginDebugControlRpcService.ExchangeAsync(byte[] message, CancellationToken cancellationToken);
ValueTask IPluginDebugEventRpcService.PublishAsync(byte[] message, CancellationToken cancellationToken);
```

The host provides control; the plugin provides events. Both services use the plugin's existing authenticated peer
and are scoped to the `PluginSession` created for that connection.

## Envelope

Every request, response, and event is one bounded UTF-8 JSON object:

```json
{
  "version": 1,
  "kind": "initialize",
  "id": "client-generated-correlation-id",
  "sessionToken": "64-lowercase-hex-characters",
  "payload": {}
}
```

`version` is a positive 32-bit integer. `kind`, `id`, and `sessionToken` contain 1–128 characters. `payload` is
required. Field names are case-sensitive, duplicate envelope fields are rejected, JSON depth is limited to 32,
and the host's `MaxMessageBytes` limit applies before deserialization.

The server mints a random 256-bit session token and publishes it in the reverse `session` bootstrap event. Every
control request is checked with a fixed-time token comparison before dispatch. A token from another plugin
session cannot inspect, mutate, pause, resume, or replace that session's kernels.

Successful responses preserve the request `id`, use `<command>Response` as `kind`, and contain:

```json
{ "success": true, "body": {} }
```

Errors use `kind: "error"`:

```json
{ "success": false, "error": { "code": "staleRun", "message": "..." } }
```

Malformed input is fail-closed with stable codes such as `invalidMessage`, `messageTooLarge`, `invalidVersion`,
`unsupportedVersion`, `unauthorized`, `unsupportedCommand`, and command-specific validation errors. Error text
is safe protocol text, not a server exception or stack trace.

## Negotiation

Send `initialize` first. Its response reports:

- `supported`, `protocolVersion`, and `supportedVersions`;
- the exact `commands` enabled by this host;
- `defaultPauseScope` and `allowedPauseScopes`;
- `stopLeaseMilliseconds`;
- evaluator `id`, `trustProfile`, `supportsAwait`, and `supportsAssemblyUpload`; and
- snapshot, expression, assembly-upload, and message limits.

An older or disabled server can therefore run the kernel normally while reporting remote debugging unavailable.
Clients must use advertised commands and capabilities rather than inferring them from a tooling version.
`uploadAssembly` is advertised only for a trusted evaluator.

Send `attach` with an optional lowercase `pauseScope` value: `server`, `pluginSession`, or `execution`. The host
allow-list remains authoritative. Version 1 permits one attached debugger per `PluginServer`; another attach
receives `debuggerAlreadyAttached`.

## Commands

| Kind | Required payload | Result or effect |
| --- | --- | --- |
| `initialize` | `{}` | Capabilities, scopes, evaluator, lease, and limits. |
| `attach` | optional `pauseScope` | Acquires the server debugger slot. |
| `setBreakpoints` | `pluginId`; `nodeIds[]` or `breakpoints[]` | Replaces package breakpoints and reports verification. |
| `pause` | `{}` | Stops an owned run at its next safe checkpoint. |
| `continue` | `runId` | Resumes one stopped run. |
| `stepIn` / `stepOver` / `stepOut` | `runId` | Installs a logical-frame step plan and resumes. |
| `threads` | `{}` | Lists only stopped runs owned by the authenticated session. |
| `stackTrace` | `runId` | Returns logical sandbox frames and structural node IDs. |
| `variables` | `frameId` | Returns bounded argument/local snapshots. |
| `setVariable` | `frameId`, `name`, typed `value` | Validates and replaces an existing slot. |
| `evaluate` | `frameId`, `expression`, optional `allowAwait` | Evaluates with the negotiated trust profile. |
| `setExpression` | `frameId`, variable-name `expression`, `valueExpression` | Evaluates the value and applies validated sandbox mutation. |
| `uploadAssembly` | `fileName`, base64 `content`, `offset`, `complete` | Appends a bounded chunk to a session-temporary trusted assembly. |
| `heartbeat` | `{}` | Renews the stop lease. |
| `disconnect` | `{}` | Detaches, clears temporary state, and resumes safely. |

A structured breakpoint contains `nodeId` and optional `condition`, positive `hitCount`, or `logMessage`.
Conditions and logpoint interpolation use the negotiated evaluator. Node IDs are versioned structural identities;
they do not depend on source spans, package JSON formatting, or canonical hashes.

`runId` and `frameId` are opaque. Commands using a completed, foreign, or superseded identifier return
`staleRun`/`staleFrame`. Clients must not manufacture them.

Typed debug values are sandbox values, not arbitrary JSON objects. Collection/record/map expansion is bounded by
the host snapshot limit. Mutation must match the existing slot's `SandboxType` and all sandbox resource limits.

## Reverse events

The reverse endpoint publishes authenticated envelopes. Version 1 defines:

- `session`: bootstrap containing `protocolVersion` and `sessionToken`;
- `stopped`: `runId`, `pluginId`, `nodeId`, checkpoint kind, reason, and optional safe sandbox error; and
- `output`: console or stderr text for logpoints and evaluator diagnostics.

The DAP adapter converts these messages to standard DAP lifecycle, thread, stack, variable, breakpoint, and output
events. DAP is a local adapter contract; it is not the frozen server wire protocol.

## Lease and failure semantics

Every authenticated exchange renews the configured stop lease (five minutes by default). Expiry, reverse publish
failure, bridge loss, session disposal, or explicit disconnect:

1. cancels in-flight event publication;
2. discards stops, step plans, and uploaded assemblies;
3. removes debug hooks;
4. releases all server/session/execution dispatch gates; and
5. resumes stopped interpreter runs after accounting for paused wall time.

Fuel, allocation, loop, and host-call accounting continue to enforce their original budgets. Debugging never
grants a capability and never exposes another authenticated session's frames, including during a whole-server
pause.

See [Remote kernel debugging](/concepts/remote-kernel-debugging/) for host configuration and IDE setup.
