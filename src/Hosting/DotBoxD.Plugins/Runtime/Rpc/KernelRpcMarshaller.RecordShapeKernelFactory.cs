using System.Reflection;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static class RecordShapeKernelFactory
    {
        private static readonly MethodInfo FromKernelRpcValueMethod =
            typeof(KernelRpcMarshaller).GetMethod(
                nameof(FromKernelRpcValue),
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                [typeof(KernelRpcValue), typeof(Type)],
                null)!;

        public static Func<KernelRpcValue, object>? Create(
            ConstructorInfo? constructor,
            IReadOnlyList<int> constructorMap,
            IReadOnlyList<PropertyInfo> fields)
        {
            if (constructor is null)
            {
                return null;
            }

            var value = LinqExpression.Parameter(typeof(KernelRpcValue), "value");
            var parameters = constructor.GetParameters();
            var arguments = new LinqExpression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = constructorMap[i];
                var kernelField = LinqExpression.Call(
                    value,
                    nameof(KernelRpcValue.GetItem),
                    Type.EmptyTypes,
                    LinqExpression.Constant(fieldIndex));
                arguments[i] = LinqExpression.Convert(
                    ReadKernelField(kernelField, fields[fieldIndex].PropertyType),
                    parameters[i].ParameterType);
            }

            var body = LinqExpression.Convert(LinqExpression.New(constructor, arguments), typeof(object));
            return LinqExpression.Lambda<Func<KernelRpcValue, object>>(body, value).Compile();
        }

        private static LinqExpression ReadKernelField(LinqExpression kernelField, Type fieldType)
        {
            if (fieldType == typeof(bool)) return LinqExpression.Property(kernelField, nameof(KernelRpcValue.BoolValue));
            if (fieldType == typeof(int)) return LinqExpression.Property(kernelField, nameof(KernelRpcValue.Int32Value));
            if (fieldType == typeof(long)) return LinqExpression.Property(kernelField, nameof(KernelRpcValue.Int64Value));
            if (fieldType == typeof(double)) return LinqExpression.Property(kernelField, nameof(KernelRpcValue.DoubleValue));
            if (fieldType == typeof(string)) return LinqExpression.Property(kernelField, nameof(KernelRpcValue.TextValue));
            if (fieldType == typeof(Guid)) return LinqExpression.Property(kernelField, nameof(KernelRpcValue.GuidValue));
            if (fieldType.IsEnum)
            {
                var propertyName = EnumUsesI64(fieldType) ? nameof(KernelRpcValue.Int64Value) : nameof(KernelRpcValue.Int32Value);
                return LinqExpression.Convert(LinqExpression.Property(kernelField, propertyName), fieldType);
            }

            return LinqExpression.Call(
                FromKernelRpcValueMethod,
                kernelField,
                LinqExpression.Constant(fieldType, typeof(Type)));
        }
    }
}
