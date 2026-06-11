# Safe-IR

## Plugin Addendum Examples

The addendum implementation lives in `src/SafeIR.Plugins`.

Run the local live-kernel example:

```powershell
dotnet run --project examples\SafeIR.PluginLocal\SafeIR.PluginLocal.csproj
```

Run the real named-pipe IPC sample with ShaRPC:

```powershell
dotnet run --project examples\SafeIR.PluginIpc.Server\SafeIR.PluginIpc.Server.csproj -- safe-ir-plugin-ipc
dotnet run --project examples\SafeIR.PluginIpc.Client\SafeIR.PluginIpc.Client.csproj -- safe-ir-plugin-ipc
```

See `docs\Specs\Addendum\Examples.md` for details.
