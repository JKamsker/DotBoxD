namespace DotBoxD.Kernels.Tests.Policy;

internal readonly struct DualParameterFormatter : IConvertible, IFormattable
{
    string IConvertible.ToString(IFormatProvider? provider) => "convertible";
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => "formattable";
    TypeCode IConvertible.GetTypeCode() => TypeCode.Object;
    bool IConvertible.ToBoolean(IFormatProvider? provider) => throw new NotSupportedException();
    byte IConvertible.ToByte(IFormatProvider? provider) => throw new NotSupportedException();
    char IConvertible.ToChar(IFormatProvider? provider) => throw new NotSupportedException();
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new NotSupportedException();
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => throw new NotSupportedException();
    double IConvertible.ToDouble(IFormatProvider? provider) => throw new NotSupportedException();
    short IConvertible.ToInt16(IFormatProvider? provider) => throw new NotSupportedException();
    int IConvertible.ToInt32(IFormatProvider? provider) => throw new NotSupportedException();
    long IConvertible.ToInt64(IFormatProvider? provider) => throw new NotSupportedException();
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => throw new NotSupportedException();
    float IConvertible.ToSingle(IFormatProvider? provider) => throw new NotSupportedException();
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => throw new NotSupportedException();
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => throw new NotSupportedException();
    uint IConvertible.ToUInt32(IFormatProvider? provider) => throw new NotSupportedException();
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => throw new NotSupportedException();
}
