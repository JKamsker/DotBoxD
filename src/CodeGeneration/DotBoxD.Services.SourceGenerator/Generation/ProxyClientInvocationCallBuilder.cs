using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class ProxyClientInvocationCallBuilder
{
    public static ProxyClientInvocationCall Build(
        MethodModel method,
        List<ParameterModel> requestParameters,
        Dictionary<int, string> streamHandles,
        string? returnType,
        bool hasReturn,
        string serviceName,
        string rpcName,
        string ctArg,
        string? streamArgument,
        CancellationToken ct)
    {
        if (requestParameters.Count == 0)
        {
            return BuildParameterless(method.ReturnKind, returnType, hasReturn, serviceName, rpcName, ctArg);
        }

        if (requestParameters.Count == 1)
        {
            return BuildSingleParameter(
                method.ReturnKind,
                requestParameters[0],
                streamHandles,
                returnType,
                hasReturn,
                serviceName,
                rpcName,
                ctArg,
                streamArgument);
        }

        return BuildTuple(
            method.ReturnKind,
            requestParameters,
            streamHandles,
            returnType,
            hasReturn,
            serviceName,
            rpcName,
            ctArg,
            streamArgument,
            ct);
    }

    private static ProxyClientInvocationCall BuildParameterless(
        MethodReturnKind returnKind,
        string? returnType,
        bool hasReturn,
        string serviceName,
        string rpcName,
        string ctArg)
        => new(
            BuildTypeArgs(returnKind, requestType: null, returnType, hasReturn),
            $"\"{serviceName}\", \"{rpcName}\", {ctArg}",
            $"\"{serviceName}\", this._instanceId!, \"{rpcName}\", {ctArg}");

    private static ProxyClientInvocationCall BuildSingleParameter(
        MethodReturnKind returnKind,
        ParameterModel parameter,
        Dictionary<int, string> streamHandles,
        string? returnType,
        bool hasReturn,
        string serviceName,
        string rpcName,
        string ctArg,
        string? streamArgument)
    {
        var wireType = ProxyGenerationHelpers.GetWireType(parameter);
        var wireArgument = GetWireArgument(parameter, requestIndex: 0, streamHandles);
        var streamArg = StreamArgumentSuffix(returnKind, streamArgument);
        return new ProxyClientInvocationCall(
            BuildTypeArgs(returnKind, wireType, returnType, hasReturn),
            $"\"{serviceName}\", \"{rpcName}\", {wireArgument}{streamArg}, {ctArg}",
            $"\"{serviceName}\", this._instanceId!, \"{rpcName}\", {wireArgument}{streamArg}, {ctArg}");
    }

    private static ProxyClientInvocationCall BuildTuple(
        MethodReturnKind returnKind,
        List<ParameterModel> requestParameters,
        Dictionary<int, string> streamHandles,
        string? returnType,
        bool hasReturn,
        string serviceName,
        string rpcName,
        string ctArg,
        string? streamArgument,
        CancellationToken ct)
    {
        var tupleTypes = new StringBuilder();
        var tupleValues = new StringBuilder();
        for (var i = 0; i < requestParameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
            {
                tupleTypes.Append(", ");
                tupleValues.Append(", ");
            }

            tupleTypes.Append(ProxyGenerationHelpers.GetWireType(requestParameters[i]));
            tupleValues.Append(GetWireArgument(requestParameters[i], i, streamHandles));
        }

        var streamArg = StreamArgumentSuffix(returnKind, streamArgument);
        return new ProxyClientInvocationCall(
            BuildTypeArgs(returnKind, $"({tupleTypes})", returnType, hasReturn),
            $"\"{serviceName}\", \"{rpcName}\", ({tupleValues}){streamArg}, {ctArg}",
            $"\"{serviceName}\", this._instanceId!, \"{rpcName}\", ({tupleValues}){streamArg}, {ctArg}");
    }

    private static string StreamArgumentSuffix(MethodReturnKind returnKind, string? streamArgument)
        => NeedsStreamArgument(returnKind, streamArgument)
            ? $", {streamArgument ?? NullStreamArray()}"
            : string.Empty;

    private static string BuildTypeArgs(
        MethodReturnKind returnKind,
        string? requestType,
        string? returnType,
        bool hasReturn)
    {
        if (NamingHelpers.IsAsyncEnumerableReturn(returnKind))
        {
            return requestType is null
                ? $"<{returnType}>"
                : $"<{requestType}, {returnType}>";
        }

        if (NamingHelpers.IsStreamReturn(returnKind) || NamingHelpers.IsPipeReturn(returnKind))
        {
            return requestType is null ? string.Empty : $"<{requestType}>";
        }

        if (requestType is null)
        {
            return hasReturn ? $"<{returnType}>" : string.Empty;
        }

        return hasReturn ? $"<{requestType}, {returnType}>" : $"<{requestType}>";
    }

    private static string NullStreamArray() =>
        $"({ServicesGeneratorTypeNames.ArrayOf(ServicesGeneratorTypeNames.GlobalRpcStreamAttachment)}?)null";

    private static bool NeedsStreamArgument(MethodReturnKind returnKind, string? streamArgument) =>
        streamArgument is not null ||
        NamingHelpers.IsStreamReturn(returnKind) ||
        NamingHelpers.IsPipeReturn(returnKind) ||
        NamingHelpers.IsAsyncEnumerableReturn(returnKind);

    private static string GetWireArgument(
        ParameterModel parameter,
        int requestIndex,
        Dictionary<int, string> streamHandles) =>
        parameter.StreamKind == ParameterStreamKind.None
            ? ProxyGenerationHelpers.GetWireArgument(parameter)
            : streamHandles[requestIndex];
}

internal readonly struct ProxyClientInvocationCall
{
    public ProxyClientInvocationCall(string typeArgs, string singletonArguments, string instanceArguments)
    {
        TypeArgs = typeArgs;
        SingletonArguments = singletonArguments;
        InstanceArguments = instanceArguments;
    }

    public string TypeArgs { get; }

    public string SingletonArguments { get; }

    public string InstanceArguments { get; }
}
