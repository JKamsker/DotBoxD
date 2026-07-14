using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

/// <summary>
/// Generates client proxy classes for DotBoxD services. The generated proxy implements
/// the user's interface exactly — same return types, same parameter list, only adding
/// a forwarding body to <c>IRpcInvoker</c>. All emitted
/// type references are fully qualified with <c>global::</c> so the generated file
/// never depends on the user's <c>using</c> set.
/// </summary>
internal static partial class ProxyGenerator
{
    public static string Generate(
        ServiceModel service,
        EquatableArray<AsyncSiblingMethod> siblingMethods,
        bool emitClsNonCompliantAttribute,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var proxyName = NamingHelpers.StripInterfacePrefix(service.InterfaceName) + "Proxy";
        var qualifiedInterface = QualifyServiceType(service, service.InterfaceName);
        var qualifiedAsyncSibling = QualifyServiceType(
            service,
            NamingHelpers.AsyncSiblingInterfaceName(service.InterfaceName));
        var baseList = ProxyBaseList(qualifiedInterface, qualifiedAsyncSibling, siblingMethods);
        AppendProxyHeader(sb, service, proxyName, baseList, emitClsNonCompliantAttribute);
        AppendProxyFields(sb, service, ct);
        AppendProxyConstructors(sb, service, proxyName, ct);
        AppendProxyProperties(sb, service, ct);
        AppendProxyMethods(sb, service, proxyName, ct);
        AppendAsyncSiblingMethods(sb, service, siblingMethods, proxyName, qualifiedAsyncSibling, ct);
        AppendProxyFooter(sb, service);
        return sb.ToString();
    }
    private static string QualifyServiceType(ServiceModel service, string typeName) =>
        IdentifierHelpers.QualifyTypeName(service.Namespace, typeName);

    private static string PropertyFieldName(ServicePropertyModel property) =>
        "__dotboxd_" + IdentifierHelpers.UnescapeIdentifier(property.Name);

    private static void GenerateProxyMethod(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string proxyName,
        CancellationToken ct)
    {
        var paramList = new StringBuilder();
        ProxyGenerationHelpers.AppendParameterList(paramList, method.Parameters, ct);

        var declaredReturn = method.DeclaredReturnType;
        // Stubs stay non-async so out-parameters are definitely assigned by throw.
        var asyncKeyword = method.UnsupportedReason is null && RequiresAsyncStateMachine(method.ReturnKind)
            ? "async "
            : string.Empty;
        var unsafeKeyword = method.RequiresUnsafeSignature ? "unsafe " : string.Empty;
        var ctArg = ProxyGenerationHelpers.GetCancellationTokenArgument(method.Parameters, ct);
        var explicitInterface = MethodRequiresExplicitImplementation(method.Name, proxyName);
        var access = explicitInterface ? string.Empty : "public ";
        var target = explicitInterface ? method.ExplicitImplementationType + "." + method.Name : method.Name;

        ProxyGenerationHelpers.AppendAttributes(sb, method.MemberAttributePrefix);
        ProxyGenerationHelpers.AppendAttributes(sb, method.ReturnAttributePrefix);
        sb.AppendLine($"        {access}{unsafeKeyword}{asyncKeyword}{method.ReturnRefKindKeyword}{declaredReturn} {target}{method.TypeParameterList}({paramList}){method.ConstraintClauses}");
        sb.AppendLine("        {");

        if (method.UnsupportedReason is not null)
        {
            sb.AppendLine($"            throw new {ServicesGeneratorTypeNames.GlobalNotSupportedException}(\"DotBoxD cannot marshal '{IdentifierHelpers.UnescapeIdentifier(method.Name)}': {LiteralHelpers.EscapeStringLiteral(method.UnsupportedReason)}\");");
        }
        else
        {
            var locals = new GeneratedLocalNames(method.Parameters, ct);
            if (method.ReturnKind == MethodReturnKind.AsyncEnumerable &&
                HasStreamedRequestParameter(method, ct))
            {
                EmitLazyAsyncEnumerableInvocation(sb, service, method, ctArg, locals, ct);
            }
            else
            {
                var invocation = BuildClientInvocation(sb, service, method, ctArg, locals, ct);
                ProxyInvocationCleanupEmitter.EmitProxyInvocation(
                    sb,
                    method,
                    invocation.Invocation,
                    invocation.Reservations,
                    locals,
                    ct);
            }
        }

        sb.AppendLine("        }");
    }

    /// <summary>
    /// Emits a non-blocking proxy method that satisfies the async sibling interface.
    /// Reuses the underlying RPC call site of the original method (same service, same
    /// wire method name) but returns a Task / Task&lt;T&gt; so the caller never blocks.
    /// </summary>
    private static void GenerateAsyncSiblingMethod(
        StringBuilder sb,
        ServiceModel service,
        AsyncSiblingMethod s,
        string proxyName,
        string qualifiedAsyncSibling,
        CancellationToken ct)
    {
        var paramList = new StringBuilder();
        ProxyGenerationHelpers.AppendParameterList(paramList, s.Parameters, ct);

        var declaredReturn = NamingHelpers.GetDeclaredReturnTypeText(
            s.SiblingReturnKind, s.Source.UnwrappedReturnType);
        var explicitInterface = MethodRequiresExplicitImplementation(s.Name, proxyName);
        var access = explicitInterface ? string.Empty : "public ";
        var target = explicitInterface ? qualifiedAsyncSibling + "." + s.Name : s.Name;

        var asyncKeyword = RequiresAsyncStateMachine(s.SiblingReturnKind) ? "async " : string.Empty;
        ProxyGenerationHelpers.AppendAttributes(sb, s.Source.MemberAttributePrefix);
        sb.AppendLine($"        {access}{asyncKeyword}{declaredReturn} {target}({paramList})");
        sb.AppendLine("        {");

        // For the wire call, treat the sibling as having a CancellationToken AND the
        // sibling's return kind (so the invocation picks the right overload).
        var virtualSource = s.Source with
        {
            HasCancellationToken = true,
            ReturnKind = s.SiblingReturnKind,
            Parameters = s.Parameters,
        };
        var locals = new GeneratedLocalNames(s.Parameters, ct);
        var ctArg = ProxyGenerationHelpers.GetCancellationTokenArgument(s.Parameters, ct);
        if (virtualSource.ReturnKind == MethodReturnKind.AsyncEnumerable &&
            HasStreamedRequestParameter(virtualSource, ct))
        {
            EmitLazyAsyncEnumerableInvocation(sb, service, virtualSource, ctArg, locals, ct);
        }
        else
        {
            var invocation = BuildClientInvocation(sb, service, virtualSource, ctArg, locals, ct);
            ProxyInvocationCleanupEmitter.EmitProxyInvocation(
                sb,
                virtualSource,
                invocation.Invocation,
                invocation.Reservations,
                locals,
                ct);
        }

        sb.AppendLine("        }");
    }

    private static bool RequiresAsyncStateMachine(MethodReturnKind returnKind) =>
        returnKind is MethodReturnKind.TaskOfSubService or MethodReturnKind.ValueTaskOfSubService;

}
