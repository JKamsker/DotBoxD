using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class MemoryExtensionsIdentitySurpriseTests
{
    [Fact]
    public void Array_contains_from_the_framework_still_lowers_to_in()
    {
        var values = new[] { 5 };

        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            eventData => values.Contains(eventData.Damage));

        Assert.Equal(QueryFilterKind.In, filter.Kind);
        Assert.Equal(nameof(AttackTestEvent.Damage), filter.Field);
        Assert.Single(filter.Values);
    }

    [Fact]
    public void Foreign_memory_extensions_contains_is_rejected_instead_of_lowering_to_in()
    {
        var eventParameter = System.Linq.Expressions.Expression.Parameter(typeof(AttackTestEvent), "eventData");
        var foreignContains = CreateForeignMemoryExtensionsContainsMethod();
        var call = System.Linq.Expressions.Expression.Call(
            foreignContains,
            System.Linq.Expressions.Expression.Constant(new[] { 5 }),
            System.Linq.Expressions.Expression.Property(eventParameter, nameof(AttackTestEvent.Damage)));
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<AttackTestEvent, bool>>(call, eventParameter);

        Action translate = () => _ = ExpressionQueryTranslator.TranslateFilter(predicate);
        var exception = Assert.Throws<QueryTranslationException>(translate);

        Assert.Contains("custom static Contains methods are not supported", exception.Message, StringComparison.Ordinal);
    }

    private static MethodInfo CreateForeignMemoryExtensionsContainsMethod()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"ForeignMemoryExtensions{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("ForeignMemoryExtensions");
        var type = module.DefineType(
            "System.MemoryExtensions",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod(
            nameof(Enumerable.Contains),
            MethodAttributes.Public | MethodAttributes.Static);
        var itemType = method.DefineGenericParameters("T")[0];
        method.SetReturnType(typeof(bool));
        method.SetParameters(itemType.MakeArrayType(), itemType);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldc_I4_0);
        generator.Emit(OpCodes.Ret);

        return type.CreateType()!
            .GetMethod(nameof(Enumerable.Contains), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(typeof(int));
    }
}
