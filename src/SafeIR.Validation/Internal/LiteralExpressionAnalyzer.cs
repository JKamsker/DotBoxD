namespace SafeIR.Validation.Internal;

using SafeIR;

internal static class LiteralExpressionAnalyzer
{
    private const int MaxTextLiteralLength = 65_536;

    public static SandboxType Analyze(LiteralExpression literal, ref SandboxEffect effects)
    {
        ValidateLiteralValue(literal.Value);

        if (literal.Value is StringValue text)
        {
            effects |= SandboxEffect.Alloc;
            EnsureTextLiteralLength(text.Value, "string");
        }

        if (literal.Value is OpaqueIdValue id)
        {
            effects |= SandboxEffect.Alloc;
            EnsureTextLiteralLength(id.Value, id.TypeName);
            if (!SandboxLiteralConstraints.IsOpaqueId(id.Value) ||
                !SandboxType.IsKnownOpaqueId(id.TypeName))
            {
                throw InvalidLiteral("E-CONST-ID", "opaque ID constant is invalid");
            }
        }

        if (literal.Value is F64Value number && !double.IsFinite(number.Value))
        {
            throw InvalidLiteral("E-CONST-F64", "f64 constant must be finite");
        }

        if (literal.Value is SandboxPathValue path)
        {
            effects |= SandboxEffect.Alloc;
            EnsureTextLiteralLength(path.Value.RelativePath, "path");
            if (!SandboxLiteralConstraints.IsPortableRelativePath(path.Value.RelativePath))
            {
                throw InvalidLiteral("E-CONST-PATH", "path constant must be a portable relative path");
            }
        }

        if (literal.Value is SandboxUriValue uri)
        {
            effects |= SandboxEffect.Alloc;
            EnsureTextLiteralLength(uri.Value.Value, "uri");
            if (!IsSandboxUri(uri.Value.Value))
            {
                throw InvalidLiteral("E-CONST-URI", "uri constant must be an absolute URI without user info");
            }
        }

        if (literal.Value is ListValue or MapValue)
        {
            effects |= SandboxEffect.Alloc;
        }

        return literal.Value.Type;
    }

    private static void EnsureTextLiteralLength(string value, string literalKind)
    {
        if (value.Length > MaxTextLiteralLength)
        {
            throw InvalidLiteral("E-CONST-HUGE", $"{literalKind} constant exceeds maximum length");
        }
    }

    private static bool IsSandboxUri(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Contains('\\') &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo);

    private static void ValidateLiteralValue(SandboxValue value)
    {
        try
        {
            SandboxValueValidator.RequireType(
                value,
                value.Type,
                SandboxErrorCode.ValidationError,
                "literal constant is invalid");
        }
        catch (SandboxRuntimeException)
        {
            throw InvalidLiteral("E-CONST-VALUE", "literal constant is invalid");
        }
    }

    private static SandboxValidationException InvalidLiteral(string code, string message)
        => new([new SandboxDiagnostic(code, message)]);
}
