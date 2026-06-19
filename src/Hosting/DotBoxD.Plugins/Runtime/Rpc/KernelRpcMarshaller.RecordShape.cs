using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private sealed class RecordShape
    {
        private static readonly MethodInfo FromSandboxValueMethod =
            typeof(KernelRpcMarshaller).GetMethod(
                nameof(FromSandboxValue),
                BindingFlags.Public | BindingFlags.Static,
                null,
                [typeof(SandboxValue), typeof(Type)],
                null)!;
        private static readonly MethodInfo ReadBoolMethod = ScalarReader(nameof(ReadBool));
        private static readonly MethodInfo ReadInt32Method = ScalarReader(nameof(ReadInt32));
        private static readonly MethodInfo ReadInt64Method = ScalarReader(nameof(ReadInt64));
        private static readonly MethodInfo ReadDoubleMethod = ScalarReader(nameof(ReadDouble));
        private static readonly MethodInfo ReadStringMethod = ScalarReader(nameof(ReadString));
        private static readonly MethodInfo ReadGuidMethod = ScalarReader(nameof(ReadGuid));

        private readonly ConstructorInfo? _constructor;
        private readonly int[] _constructorMap;
        private readonly Func<object, object?>[] _getters;
        private readonly Func<RecordValue, object>? _recordFactory;
        private readonly Type _type;

        public RecordShape(Type type, PropertyInfo[] fields)
        {
            _type = type;
            Fields = fields;
            _getters = CreateGetters(fields);
            (_constructor, _constructorMap) = FindConstructor(type, fields);
            _recordFactory = CreateRecordFactory(_constructor, _constructorMap, fields);
        }

        public IReadOnlyList<PropertyInfo> Fields { get; }

        public object? GetValue(object instance, int index)
            => _getters[index](instance);

        public object Construct(RecordValue record)
        {
            if (_recordFactory is not null)
            {
                return _recordFactory(record);
            }

            var arguments = new object?[Fields.Count];
            for (var i = 0; i < Fields.Count; i++)
            {
                arguments[i] = FromSandboxValue(record.Fields[i], Fields[i].PropertyType);
            }

            return ConstructFromArguments(arguments);
        }

        private object ConstructFromArguments(object?[] arguments)
        {
            var instance = Activator.CreateInstance(_type)
                ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
            for (var i = 0; i < Fields.Count; i++)
            {
                Fields[i].SetValue(instance, arguments[i]);
            }

            return instance;
        }

        private static Func<RecordValue, object>? CreateRecordFactory(
            ConstructorInfo? constructor,
            IReadOnlyList<int> constructorMap,
            IReadOnlyList<PropertyInfo> fields)
        {
            if (constructor is null)
            {
                return null;
            }

            var record = LinqExpression.Parameter(typeof(RecordValue), "record");
            var recordFields = LinqExpression.Property(record, nameof(RecordValue.Fields));
            var parameters = constructor.GetParameters();
            var arguments = new LinqExpression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = constructorMap[i];
                var sandboxField = LinqExpression.Property(
                    recordFields,
                    "Item",
                    LinqExpression.Constant(fieldIndex));
                arguments[i] = LinqExpression.Convert(
                    ReadSandboxField(sandboxField, fields[fieldIndex].PropertyType),
                    parameters[i].ParameterType);
            }

            var body = LinqExpression.Convert(LinqExpression.New(constructor, arguments), typeof(object));
            return LinqExpression.Lambda<Func<RecordValue, object>>(body, record).Compile();
        }

        private static LinqExpression ReadSandboxField(LinqExpression sandboxField, Type fieldType)
        {
            if (fieldType == typeof(bool)) return LinqExpression.Call(ReadBoolMethod, sandboxField);
            if (fieldType == typeof(int)) return LinqExpression.Call(ReadInt32Method, sandboxField);
            if (fieldType == typeof(long)) return LinqExpression.Call(ReadInt64Method, sandboxField);
            if (fieldType == typeof(double)) return LinqExpression.Call(ReadDoubleMethod, sandboxField);
            if (fieldType == typeof(string)) return LinqExpression.Call(ReadStringMethod, sandboxField);
            if (fieldType == typeof(Guid)) return LinqExpression.Call(ReadGuidMethod, sandboxField);

            return LinqExpression.Call(
                FromSandboxValueMethod,
                sandboxField,
                LinqExpression.Constant(fieldType, typeof(Type)));
        }

        private static Func<object, object?>[] CreateGetters(IReadOnlyList<PropertyInfo> fields)
        {
            var getters = new Func<object, object?>[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                getters[i] = CreateGetter(fields[i]);
            }

            return getters;
        }

        private static Func<object, object?> CreateGetter(PropertyInfo property)
        {
            var instance = LinqExpression.Parameter(typeof(object), "instance");
            var typedInstance = LinqExpression.Convert(instance, property.DeclaringType!);
            var value = LinqExpression.Property(typedInstance, property);
            var boxed = LinqExpression.Convert(value, typeof(object));
            return LinqExpression.Lambda<Func<object, object?>>(boxed, instance).Compile();
        }

        private static MethodInfo ScalarReader(string name)
            => typeof(RecordShape).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

        private static bool ReadBool(SandboxValue value)
            => value is BoolValue typed ? typed.Value : throw CannotMarshalScalar(value, typeof(bool));

        private static int ReadInt32(SandboxValue value)
            => value is I32Value typed ? typed.Value : throw CannotMarshalScalar(value, typeof(int));

        private static long ReadInt64(SandboxValue value)
            => value is I64Value typed ? typed.Value : throw CannotMarshalScalar(value, typeof(long));

        private static double ReadDouble(SandboxValue value)
            => value is F64Value typed ? typed.Value : throw CannotMarshalScalar(value, typeof(double));

        private static string ReadString(SandboxValue value)
            => value is StringValue typed ? typed.Value : throw CannotMarshalScalar(value, typeof(string));

        private static Guid ReadGuid(SandboxValue value)
            => value is GuidValue typed ? typed.Value : throw CannotMarshalScalar(value, typeof(Guid));

        private static NotSupportedException CannotMarshalScalar(SandboxValue value, Type type)
            => new($"Server extension cannot marshal sandbox value '{value.Type}' to type '{type}'.");

        private static (ConstructorInfo? Constructor, int[] Map) FindConstructor(
            Type type,
            IReadOnlyList<PropertyInfo> fields)
        {
            foreach (var constructor in type.GetConstructors())
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length != fields.Count || parameters.Length == 0)
                {
                    continue;
                }

                var map = new int[parameters.Length];
                var assigned = new bool[parameters.Length];
                if (TryMapConstructor(parameters, fields, map, assigned))
                {
                    return (constructor, map);
                }
            }

            return (null, []);
        }

        private static bool TryMapConstructor(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<PropertyInfo> fields,
            int[] map,
            bool[] assigned)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0 ||
                    assigned[fieldIndex] ||
                    parameters[i].ParameterType != fields[fieldIndex].PropertyType)
                {
                    return false;
                }

                map[i] = fieldIndex;
                assigned[fieldIndex] = true;
            }

            return true;
        }

        private static int FieldIndex(IReadOnlyList<PropertyInfo> fields, string? name)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            var match = -1;
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match >= 0)
                {
                    return -1;
                }

                match = i;
            }

            return match;
        }
    }
}
