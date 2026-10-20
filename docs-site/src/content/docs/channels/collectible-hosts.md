---
title: Unloadable RPC hosts
description: Use generated services and MessagePack DTO formatters in collectible AssemblyLoadContexts.
---

DotBoxD's service factory and kernel package registries use weak type keys. Their factory delegates
and metadata can reference the registered type without keeping an otherwise unused collectible
assembly alive. This includes generated registrations, explicit registrations, and cached kernel
package reflection factories. Discovery catalogs also follow assembly lifetime.

## Scope the MessagePack serializer

Create one serializer/options instance per collectible context, using an explicit generated resolver
from that extension assembly. Do not retain the resolver, options, serializer, proxy, dispatcher,
service instance, or DTO in the host after teardown.

```csharp
// ExtensionResolver is the extension's [GeneratedMessagePackResolver] implementation.
var options = MessagePackRpcSerializer.CreateCollectibleOptions(ExtensionResolver.Instance);
var serializer = new MessagePackRpcSerializer(options);
```

`CreateCollectibleOptions` builds a fresh composite with DotBoxD's RPC formatters, the supplied
resolvers, validated strings, native DateTime, and MessagePack's built-in scalar formatters. It retains
`MessagePackSecurity.UntrustedData`. Every DTO, union, and collection shape needs a supplied formatter;
missing formatters fail instead of falling through to reflection or dynamic formatter generation.
Custom resolvers must themselves avoid static caches that strongly reference collectible types.

`CreateWithResolver` also creates fresh options and works when the supplied resolver handles every
extension payload. Its standard/contractless fallbacks can still retain an extension if a formatter
is missing. The parameterless serializer and `CreateUnityCompatible` use those fallbacks too; use the
strict preset for unloadable hosts. A process-wide shared serializer/options instance retains its
composite formatter cache and any extension resolver supplied to it, even with generated formatters.

DotBoxD's runtime-type deserialization and constructor replay call generic MessagePack overloads
through weak-key delegates. They no longer fill MessagePack's static runtime-type delegate cache.
Closing these delegates uses reflection over generic methods, as MessagePack's runtime-type overloads
do; NativeAOT/trimming consumers must preserve the concrete generic instantiations they need. Prefer
the generic serializer APIs where the type is known statically.

## Teardown and optional deterministic cleanup

Stop outstanding calls and dispose both peers. Release every extension-owned object, then call
`context.Unload()`. Unload starts teardown; collection happens after the final strong reference is gone.
No registry cleanup call is needed for services and kernels whose registered types are collectible.

For deterministic factory removal while types remain alive, the public primitives are:

```csharp
GeneratedServiceRegistry.Unregister(serviceInterface);
GeneratedServiceRegistry.UnregisterAssembly(extensionAssembly);
KernelPackageRegistry.Unregister(kernelType);
KernelPackageRegistry.UnregisterLoadContext(context);

// Services targets netstandard2.1 and has no AssemblyLoadContext dependency.
foreach (var assembly in context.Assemblies)
    GeneratedServiceRegistry.UnregisterAssembly(assembly);
```

Each call returns the number of factories removed. Service removal and replacement advance the
registration version, so peers cannot reuse a cached proxy against a removed registration. Existing
proxy and dispatcher objects still reference the extension and must be released by the host.
Generated static constructors run once: after explicit removal, explicitly register factories again
if continuing to use the same assembly. Kernel convention resolution can rediscover its factory.
Concurrent in-flight resolution may finish using a factory it already acquired; stop calls before
deterministic cleanup.

Weak-key lifetime follows the registered service/kernel type. A collectible implementation registered
under a non-collectible host interface or kernel type still needs explicit removal, since that key
remains alive. Removal does not dispose factory-created objects or clear consumers' caches.

No version bump is needed when GC collects a weak registration: an existing proxy/type reference
keeps its key alive. Therefore no live caller can observe a registration disappear through GC.

## MessagePack's remaining responsibility

MessagePack 3.1.8's standard/source-generated resolver discovery and dynamic resolver caches can retain
assemblies or load contexts, and its internal type-key tables have no supported eviction API. DotBoxD
does not reflect into or purge those caches. Avoid reaching them with collectible payloads by supplying
complete generated formatters and using the strict preset. Related upstream reports cover
[collectible emitted types](https://github.com/MessagePack-CSharp/MessagePack-CSharp/issues/1150) and
[dynamic resolvers across load contexts](https://github.com/MessagePack-CSharp/MessagePack-CSharp/issues/1952).
These reports describe resolver/load-context limitations, rather than a supported cache-purge contract.

## Verify unload

Create/load/use/unload the context in a separate method marked `MethodImplOptions.NoInlining`, returning
only a `WeakReference` to the context. Outside that method, run a bounded loop of `GC.Collect()`,
`GC.WaitForPendingFinalizers()`, and `GC.Collect()`, and assert the reference becomes dead. Keep this
forced-GC pattern in diagnostics or tests. An async method/state machine, local variable, event handler,
or outstanding task can accidentally retain the context; clear those references before investigating
registry retention. The regression suite exercises generated proxy/dispatcher DTO round trips,
runtime-type deserialization, and both kernel factory paths in collectible contexts.
