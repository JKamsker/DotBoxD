using Mono.Cecil;

namespace DotBoxD.Plugins.Fody;

internal static class CecilExtensions
{
    public static IEnumerable<MethodDefinition> AllMethods(this ModuleDefinition module)
        => module.Types.SelectMany(AllMethods);

    public static IEnumerable<MethodDefinition> AllMethods(this TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            yield return method;
        }

        foreach (var nestedMethod in type.NestedTypes.SelectMany(AllMethods))
        {
            yield return nestedMethod;
        }
    }

    public static TypeDefinition? GetAsyncStateMachineType(this MethodDefinition method)
    {
        var attribute = method.CustomAttributes.FirstOrDefault(static candidate =>
            string.Equals(
                candidate.AttributeType.FullName,
                DotBoxDInvokeAsyncWeaverNames.AsyncStateMachineAttribute,
                StringComparison.Ordinal));

        return attribute?.ConstructorArguments.Count == 1 &&
               attribute.ConstructorArguments[0].Value is TypeReference stateMachine
            ? stateMachine.Resolve()
            : null;
    }
}
