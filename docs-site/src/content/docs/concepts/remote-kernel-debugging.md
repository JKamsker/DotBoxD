---
title: Remote kernel debugging
description: Debug the real server-side DotBoxD interpreter from Rider, Visual Studio, VS Code, or another DAP client.
---

Remote kernel debugging pauses the **actual IR interpreter in the host process** while the debugger displays the
plugin's original C# source. It does not copy execution into the plugin and does not create a local shadow run.
`RunLocal` and `RegisterLocal` remain native plugin code and use the ordinary managed debugger.

The feature is opt-in at both ends. A server that does not enable it continues to execute compatible kernels and
reports debugging as unsupported. The v1 endpoint is generic and versioned, so application control contracts do
not need debug-specific methods.

## Enable the host

Pass an explicit policy when constructing the `PluginServer`:

```csharp
using DotBoxD.Plugins;
using DotBoxD.Plugins.Debugging;

var server = PluginServer.Create(
    remoteDebugOptions: new PluginRemoteDebugOptions
    {
        Enabled = true,
        DefaultPauseScope = KernelDebugPauseScope.PluginSession,
        AllowedPauseScopes =
        [
            KernelDebugPauseScope.PluginSession,
            KernelDebugPauseScope.Execution
        ],
        StopLease = TimeSpan.FromMinutes(5),
        MaxSnapshotBytes = 1024 * 1024,
        MaxExpressionLength = 4096,
        MaxAssemblyUploadBytes = 16 * 1024 * 1024,
        MaxMessageBytes = 1024 * 1024
    });
```

`Enabled` defaults to `false`. The default host policy permits only `Server` pause scope. The host's allow-list is
authoritative even when a client requests a narrower or broader scope.

Provision the generic duplex services on the existing plugin peer. `PluginConnectionHost<TConnection>` performs
the session ownership, reverse endpoint, bootstrap, and disconnect cleanup when given an enabled option:

```csharp
await PluginConnectionHost<MyConnection>.StartAsync(
    server,
    pipeName,
    (peer, session) => ConfigureConnection(peer, session),
    new PluginConnectionDebugOptions(Enabled: true));
```

This does not change the application's control service. It adds one host-provided
`IPluginDebugControlRpcService.ExchangeAsync(byte[])` endpoint and one plugin-provided
`IPluginDebugEventRpcService.PublishAsync(byte[])` endpoint.

## Start the plugin bridge only for debug launches

The plugin-side bridge owns local source maps and tunnels the stable protocol over the plugin's existing server
connection. Start it only when launch tooling asks for kernel debugging:

```csharp
await using var debugBridge =
    Environment.GetEnvironmentVariable("DOTBOXD_KERNEL_DEBUG") == "1"
        ? PluginDebugBridge.Start()
        : null;

var builder = debugBridge is null
    ? GamePluginServerBuilder.FromPipeName(pipeName)
    : GamePluginServerBuilder.FromPipeNameWithKernelDebugging(pipeName, debugBridge);
```

The generated debugging builder is convenience over public primitives. Manual wiring can:

1. call `PluginDebugBridge.Start()`;
2. provide the bridge through `peer.ProvidePluginDebugEvents(bridge)`;
3. attach `peer.GetPluginDebugControl()` with `bridge.AttachControl(...)`;
4. call `RegisterPackage` or `PreparePackageAsync` before installation; and
5. use `InstallAsync` / `InstallServerExtensionAsync` when the bridge owns the `PluginSession` install path.

`PreparePackageAsync` registers maps before installation and, by default, waits up to 30 seconds for DAP
`configurationDone`. This closes the race where an immediately wired kernel could run before its breakpoint was
active. `PluginDebugBridgeOptions` controls that wait, source reading, and local frame size.

The local pipe uses a random high-entropy name, a separate 256-bit discovery token, and current-user-only access.
The discovery descriptor is per-user and the bridge accepts one adapter at a time.

## Attach an IDE

### Visual Studio Code

Install or run the extension in `ide/vscode-dotboxd-debug`, build `tools/DotBoxD.DebugAdapter`, and use
**DotBoxD: Pick Kernel Debug Process**. A minimal configuration is:

```json
{
  "type": "dotboxd-kernel",
  "request": "attach",
  "name": "DotBoxD kernels",
  "processId": "${command:dotboxd.pickKernelProcess}",
  "pauseScope": "PluginSession"
}
```

`pluginId` is optional. Omitting it binds a source breakpoint to every mapped package in the authenticated plugin
session. Raw DAP hosts launch `DotBoxD.DebugAdapter` over stdio and send the plugin PID in `attach`.

### JetBrains Rider

Build `ide/rider-dotboxd-debug` with its Gradle wrapper, then install the ZIP from `build/distributions` through
**Settings > Plugins > Install Plugin from Disk**. Use **Run > Attach to Process > Attach to DotBoxD Kernels** and
select the plugin PID. The picker enumerates live per-user descriptor filenames without reading their authentication
tokens; the bundled adapter performs the protected descriptor read and connection after selection.

The Rider integration mirrors enabled C# line breakpoints into the kernel DAP session. Use a compound configuration
with the normal .NET plugin and server profiles when managed `RunLocal` code and server-executed IR must be debugged
together. The **DotBoxD Kernel** run-configuration type also supports persistent PID, package-filter, pause-scope,
and development adapter-path settings.

### Visual Studio

Build and install `ide/visualstudio/DotBoxD.KernelDebug.Vsix` on Windows. Attach to the plugin process with both
**Managed (.NET)** and **DotBoxD Kernel** code types. Managed breakpoints handle native plugin code; kernel
breakpoints handle server-executed IR. Both use the same C# files.

The repository's **GameServer + Plugin + Kernel Bridge** compound demonstrates the server, managed plugin
debugger, opt-in bridge, and kernel adapter launched together.

## Source mapping behavior

The analyzer emits documents, SHA-256 checksums, exact spans, and source-variable bindings for named kernels,
`[KernelMethod]` helpers, server-extension loops and awaited calls, `InvokeAsync` captures, hook/result chains,
and composed pushdown stages. A stage duplicated into `ShouldHandle` and `Handle` is mapped in both functions.

Debug metadata remains in `KernelDebugInfo` on the plugin side. It is excluded from plugin-package JSON and the
canonical module hash. The bridge translates a C# location to deterministic structural node IDs and sends only
those IDs to the server.

If the current source's SHA-256 checksum differs from the package document, the adapter leaves its breakpoint
unverified. Handwritten or unmapped IR is still inspectable through a read-only `dotboxd-ir://` virtual source.
For handwritten mappings, use `KernelDebugDocument`, `KernelDebugModuleMapper`, `KernelDebugInfo.Create`, and
`LoweredPipelineDebugComposer`; source generators do not have privileged runtime access.

## Stops, scopes, and cleanup

- `Server` parks all DotBoxD dispatches. Foreign session frames are never exposed.
- `PluginSession` parks only kernels owned by the authenticated plugin session.
- `Execution` parks only the stopped run. Concurrent stopped runs appear as separate DAP threads.

Running interpreted kernels stop at safe checkpoints and new affected dispatches wait at a gate. Debug RPC and
unrelated host threads stay live. A debug hook forces compiled/auto kernels to the interpreter and disables node-
skipping fast paths. Wall-time deadlines are extended by stopped time; fuel, allocation, loop, and host-call
accounting are not suspended.

Debugger disconnect, bridge failure, plugin disconnect, or stop-lease expiry clears temporary assemblies and
stops, removes hooks, extends paused deadlines, opens every affected gate, and releases the server's one-debugger
slot. Worker-process **kernel execution** debugging is rejected in v1; plugin kernels use in-process prepared
execution.

## Evaluation trust profiles

`SandboxOnlyPluginDebugEvaluator` is the default. Conditions, watches, logpoints, console expressions, and writes
operate only on sandbox frame values through the side-effect-free expression grammar. No plugin assembly, C#
source, host object, or server internal is available. Writes still pass the original slot type and sandbox shape/
resource validation and cannot introduce bindings, capabilities, or CLR objects.

The optional `DotBoxD.Kernels.Debugging.Clr` package supplies two explicit host choices:

- `ClrPluginDebugEvaluators.CreateTrustedWorker(...)` evaluates full C# in a disposable child process over
  serialized frame/context data. Configure reference paths, imports, proxies, time, and memory limits.
- `ClrPluginDebugEvaluators.CreateTrustedInProcess(...)` evaluates full C# with live context objects and selected
  references inside the server process.

:::danger[TrustedInProcess grants server-process code execution]
`TrustedInProcess` is not a security boundary. Evaluated C# can execute arbitrary code with the server process's
authority and cannot simultaneously be promised that server internals are hidden. Enable it only for fully
trusted debugger users.
:::

Plugin assemblies and dependencies upload lazily only after negotiation selects a trusted profile. A safe server
does not need the CLR-debugging package and does not load or ship Roslyn.

Data breakpoints, reverse execution, hot reload, and restart-frame are outside v1.

See [Remote debug protocol v1](/reference/remote-debug-protocol/) for the frozen wire contract.
