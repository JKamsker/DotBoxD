using Mono.Cecil;

namespace DotBoxD.Plugins.Fody;

internal static class InvokeAsyncClosureCaptureWeaver
{
    public static InvokeAsyncClosureCaptureWeaveResult Rewrite(
        ModuleDefinition module,
        Action<string> info,
        Action<string> warning)
    {
        var generatedType = module.GetType(DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsFullName);
        if (generatedType is null)
        {
            return default;
        }

        var closureMap = InvokeAsyncClosureMap.Discover(module, generatedType);
        var targetGetter = module.ImportReference(
            typeof(Delegate).GetProperty(nameof(Delegate.Target))!.GetMethod);
        var rewrittenInterceptors = 0;
        var rewrittenAccesses = 0;

        foreach (var interceptor in generatedType.Methods.Where(IsInvokeAsyncInterceptor))
        {
            var rewritten = RewriteInterceptor(
                generatedType,
                interceptor,
                closureMap,
                targetGetter,
                info,
                warning);
            if (rewritten == 0)
            {
                continue;
            }

            rewrittenInterceptors++;
            rewrittenAccesses += rewritten;
        }

        return new InvokeAsyncClosureCaptureWeaveResult(rewrittenInterceptors, rewrittenAccesses);
    }

    private static int RewriteInterceptor(
        TypeDefinition generatedType,
        MethodDefinition interceptor,
        InvokeAsyncClosureMap closureMap,
        MethodReference targetGetter,
        Action<string> info,
        Action<string> warning)
    {
        var captureMethods = FindCaptureMethods(generatedType, interceptor).ToArray();
        if (captureMethods.Length == 0)
        {
            return 0;
        }

        if (!closureMap.TryGetClosure(interceptor, out var closure))
        {
            info($"DotBoxD InvokeAsync weaver kept reflection fallback for {interceptor.Name}: closure type was not provable.");
            return 0;
        }

        if (!CanExposeClosure(closure))
        {
            warning($"DotBoxD InvokeAsync weaver kept reflection fallback for {interceptor.Name}: closure type is not safely accessible.");
            return 0;
        }

        MakeClosureAssemblyVisible(closure);
        return captureMethods.Sum(method =>
            InvokeAsyncCaptureAccessRewriter.Rewrite(method, closure, targetGetter, warning));
    }

    private static IEnumerable<MethodDefinition> FindCaptureMethods(
        TypeDefinition generatedType,
        MethodDefinition interceptor)
    {
        var generatedNameMarker = "<" + interceptor.Name + ">";
        foreach (var type in SelfAndNestedTypes(generatedType))
        {
            foreach (var method in type.Methods)
            {
                if (method.HasBody &&
                    BelongsToInterceptor(method, generatedType, generatedNameMarker) &&
                    InvokeAsyncCaptureAccessRewriter.ContainsCaptureHelperCall(method))
                {
                    yield return method;
                }
            }
        }
    }

    private static bool BelongsToInterceptor(
        MethodDefinition method,
        TypeDefinition generatedType,
        string generatedNameMarker)
    {
        if (method.Name.StartsWith(generatedNameMarker, StringComparison.Ordinal))
        {
            return true;
        }

        for (var type = method.DeclaringType; type != generatedType; type = type.DeclaringType)
        {
            if (type is null)
            {
                return false;
            }

            if (type.Name.StartsWith(generatedNameMarker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<TypeDefinition> SelfAndNestedTypes(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes.SelectMany(SelfAndNestedTypes))
        {
            yield return nested;
        }
    }

    private static bool IsInvokeAsyncInterceptor(MethodDefinition method)
        => method.Name.StartsWith(DotBoxDInvokeAsyncWeaverNames.InvokeAsyncMethodPrefix, StringComparison.Ordinal);

    private static bool CanExposeClosure(TypeDefinition closure)
        => IsCompilerGenerated(closure) && IsParentChainAccessibleFromSameAssembly(closure.DeclaringType);

    private static bool IsCompilerGenerated(TypeDefinition type)
        => type.CustomAttributes.Any(static attribute =>
            string.Equals(
                attribute.AttributeType.FullName,
                DotBoxDInvokeAsyncWeaverNames.CompilerGeneratedAttribute,
                StringComparison.Ordinal));

    private static bool IsParentChainAccessibleFromSameAssembly(TypeDefinition? type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.IsNestedPrivate || current.IsNestedFamily || current.IsNestedFamilyAndAssembly)
            {
                return false;
            }
        }

        return true;
    }

    private static void MakeClosureAssemblyVisible(TypeDefinition closure)
    {
        if (!closure.IsNestedPrivate)
        {
            return;
        }

        closure.Attributes &= ~TypeAttributes.VisibilityMask;
        closure.Attributes |= TypeAttributes.NestedAssembly;
    }
}

internal readonly struct InvokeAsyncClosureCaptureWeaveResult
{
    public InvokeAsyncClosureCaptureWeaveResult(int rewrittenInterceptors, int rewrittenAccesses)
    {
        RewrittenInterceptors = rewrittenInterceptors;
        RewrittenAccesses = rewrittenAccesses;
    }

    public int RewrittenInterceptors { get; }

    public int RewrittenAccesses { get; }
}
