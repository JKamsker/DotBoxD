# Addendum Examples

Run the complete addendum sample set:

```powershell
dotnet run --project examples\Addendum\SafeIR.AddendumExamples\SafeIR.AddendumExamples.csproj
```

The project maps the examples in `docs\Specs\Addendum\Addendum.md` to runnable code:

- shared contracts: `examples\PluginIpc\SafeIR.PluginIpc.Server.Abstractions`
- simple filters and formulas: `SimpleContractExamples.cs`
- value bindings: `ValueBindingExample.cs`
- context bindings: `ContextBindingExample.cs`
- kernel classes and live state: `KernelExamples.cs` and `KernelClassExample.cs`
- manifest/admin inspection: `ManifestInspectionExample.cs`
- production JSON package upload: `JsonUploadExample.cs`
- runtime configuration: `RuntimeConfigurationExample.cs`
- hook subscriptions: `HookSubscriptionExample.cs`
- execution modes: `ExecutionModeExample.cs`
- design guidance: `DesignGuidanceExample.cs`
- invalid local-tooling fixtures: `InvalidToolingExamples.cs`

To verify the invalid local-tooling fixtures, build with the fixture symbol enabled. This is expected to fail with `SGP001` and `SGP020`:

```powershell
dotnet build examples\Addendum\SafeIR.AddendumExamples\SafeIR.AddendumExamples.csproj -p:DefineConstants=INVALID_PLUGIN_EXAMPLES
```
