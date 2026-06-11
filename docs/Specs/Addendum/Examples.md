# Addendum Implementation Examples

This branch implements the addendum as a hosting-layer plugin model:

- `SafeIR.Plugins` exposes live values, typed live contexts, kernel state, hook pipelines, plugin manifests, and safe message bindings.
- `SafeIR.PluginAnalyzer` provides local SDK diagnostics for forbidden File IO in kernels and unsupported live setting types.
- Plugin packages carry Safe IR plus manifest metadata. The server validates the package with the existing Safe IR validator before installation.
- Hook handlers run through `SandboxHost.ExecuteAsync`; the server does not load or execute plugin DLLs.

## Local Kernel Example

Run:

```powershell
dotnet run --project examples\SafeIR.PluginLocal\SafeIR.PluginLocal.csproj
```

The example installs the `fire-damage` kernel, publishes events, updates `MinDamage` and `DamageType` at runtime, and shows that future hook executions observe the latest live settings.

It also demonstrates:

- Level 1: `BindValue<T>`
- Level 2: `BindContext<TSettings>`
- Level 3: IR-backed kernel classes with manifest live settings

## Named-Pipe IPC Example

The IPC sample uses ShaRPC over named pipes:

```powershell
dotnet add package ShaRPC --version 1.0.0-ci.18
```

The example projects also reference the matching serializer and named-pipe transport packages:

- `ShaRPC.Serializers.MessagePack` `1.0.0-ci.18`
- `ShaRPC.Transports.NamedPipes` `1.0.0-ci.18`

Run the server in one terminal:

```powershell
dotnet run --project examples\SafeIR.PluginIpc.Server\SafeIR.PluginIpc.Server.csproj -- safe-ir-plugin-ipc
```

Run the client in another:

```powershell
dotnet run --project examples\SafeIR.PluginIpc.Client\SafeIR.PluginIpc.Client.csproj -- safe-ir-plugin-ipc
```

The client reads settings, publishes a matching event, changes live settings over IPC, and publishes again to prove the server-side hook pipeline uses the updated state.
