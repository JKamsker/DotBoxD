using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotBoxD.Codecs.MessagePack;

internal static class ConstructorReplayValidatorCompiler
{
    public static bool SupportsTypedEquality(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan);

    public static bool CanCompile(
        ConstructorInfo constructor,
        IReadOnlyList<PropertyInfo> parameterProperties,
        IReadOnlyList<PropertyInfo> boundProperties)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported ||
            !RuntimeFeature.IsDynamicCodeCompiled)
        {
            return false;
        }

        try
        {
            return HasAccessibleShape(constructor, parameterProperties, boundProperties);
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return false;
        }
    }

    public static Func<object, bool>? TryCreate(
        ConstructorInfo constructor,
        IReadOnlyList<PropertyInfo> parameterProperties,
        IReadOnlyList<PropertyInfo> boundProperties)
    {
        if (!CanCompile(constructor, parameterProperties, boundProperties))
        {
            return null;
        }

        try
        {
            return Create(constructor, parameterProperties, boundProperties).Compile();
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            return null;
        }
    }

    private static bool HasAccessibleShape(
        ConstructorInfo constructor,
        IReadOnlyList<PropertyInfo> parameterProperties,
        IReadOnlyList<PropertyInfo> boundProperties)
    {
        if (!constructor.IsPublic ||
            constructor.DeclaringType is not { IsVisible: true })
        {
            return false;
        }

        var parameters = constructor.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!parameters[i].ParameterType.IsVisible ||
                !IsAccessible(parameterProperties[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < boundProperties.Count; i++)
        {
            if (!IsAccessible(boundProperties[i]) ||
                !SupportsTypedEquality(boundProperties[i].PropertyType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAccessible(PropertyInfo property) =>
        property.GetMethod is { IsPublic: true } &&
        property.DeclaringType is { IsVisible: true } &&
        property.PropertyType.IsVisible;

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static Expression<Func<object, bool>> Create(
        ConstructorInfo constructor,
        IReadOnlyList<PropertyInfo> parameterProperties,
        IReadOnlyList<PropertyInfo> boundProperties)
    {
        var declaringType = constructor.DeclaringType
            ?? throw new InvalidOperationException("Constructor has no declaring type.");
        var value = Expression.Parameter(typeof(object), "value");
        var source = Expression.Variable(declaringType, "source");
        var replayed = Expression.Variable(declaringType, "replayed");
        var constructorParameters = constructor.GetParameters();
        var arguments = new Expression[parameterProperties.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var propertyValue = Expression.Property(source, parameterProperties[i]);
            arguments[i] = Expression.Convert(propertyValue, constructorParameters[i].ParameterType);
        }

        Expression valuesMatch = Expression.Constant(true);
        for (var i = 0; i < boundProperties.Count; i++)
        {
            var property = boundProperties[i];
            valuesMatch = Expression.AndAlso(
                valuesMatch,
                Equal(
                    property.PropertyType,
                    Expression.Property(source, property),
                    Expression.Property(replayed, property)));
        }

        var body = Expression.Block(
            [source, replayed],
            Expression.Assign(source, Expression.Convert(value, declaringType)),
            Expression.Assign(replayed, Expression.New(constructor, arguments)),
            valuesMatch);
        return Expression.Lambda<Func<object, bool>>(body, value);
    }

    // TryCreate reaches this only on runtimes that support and compile dynamic code. The NativeAOT
    // smoke deliberately crosses admission to keep the reflection-only fallback executable.
    private static Expression Equal(Type type, Expression left, Expression right)
    {
        var comparerType = typeof(EqualityComparer<>).MakeGenericType(type);
        var defaultComparer = comparerType.GetProperty(
            nameof(EqualityComparer<int>.Default),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Equality comparer has no default instance.");
        var equals = comparerType.GetMethod(
            nameof(EqualityComparer<int>.Equals),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [type, type],
            modifiers: null)
            ?? throw new InvalidOperationException("Equality comparer has no typed Equals method.");
        return Expression.Call(Expression.Property(null, defaultComparer), equals, left, right);
    }
}
