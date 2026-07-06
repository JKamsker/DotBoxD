using System.Globalization;

namespace DotBoxD.Queryable.Ast;

public sealed partial record QueryValue
{
    public static bool TryFromObject(object? value, out QueryValue result)
    {
        if (TryFromScalarObject(value, out result))
        {
            return true;
        }

        if (TryFromNumericObject(value, out result))
        {
            return true;
        }

        if (TryFromTemporalObject(value, out result))
        {
            return true;
        }

        if (value is Enum e)
        {
            result = FromEnum(e);
            return true;
        }

        result = Null;
        return false;
    }

    private static bool TryFromScalarObject(object? value, out QueryValue result)
    {
        switch (value)
        {
            case null:
                result = Null;
                return true;
            case bool b:
                result = FromBoolean(b);
                return true;
            case string s:
                result = FromString(s);
                return true;
            case decimal m:
                result = FromDecimal(m);
                return true;
            case Guid g:
                result = FromGuid(g);
                return true;
            default:
                result = Null;
                return false;
        }
    }

    private static bool TryFromNumericObject(object? value, out QueryValue result)
    {
        if (TryFromSignedIntegerObject(value, out result))
        {
            return true;
        }

        if (value is ulong u)
        {
            result = FromUnsignedInteger(u);
            return true;
        }

        return TryFromFloatingPointObject(value, out result);
    }

    private static bool TryFromSignedIntegerObject(object? value, out QueryValue result)
    {
        if (value is null)
        {
            result = Null;
            return false;
        }

        switch (Type.GetTypeCode(value.GetType()))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
                result = FromInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return true;
            default:
                result = Null;
                return false;
        }
    }

    private static bool TryFromFloatingPointObject(object? value, out QueryValue result)
    {
        if (value is not (float or double))
        {
            result = Null;
            return false;
        }

        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
        {
            result = Null;
            return false;
        }

        result = FromNumber(number);
        return true;
    }

    private static bool TryFromTemporalObject(object? value, out QueryValue result)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                result = FromTimestamp(dto);
                return true;
            case DateTime dt:
                result = FromTimestamp(ToOffset(dt));
                return true;
            case DateOnly d:
                result = FromTimestamp(new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
                return true;
            default:
                result = Null;
                return false;
        }
    }

    private static QueryValue FromEnum(Enum value)
        // Carry a ulong-backed enum exactly (its value may exceed long.MaxValue); other enums are signed.
        => Enum.GetUnderlyingType(value.GetType()) == typeof(ulong)
            ? FromUnsignedInteger(Convert.ToUInt64(value, CultureInfo.InvariantCulture))
            : FromInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture));
}
