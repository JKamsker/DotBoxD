namespace DotBoxD.UI;

public enum UiValueKind
{
    Boolean = 1,
    Int32 = 2,
    Number = 3,
    String = 4
}

/// <summary>Closed scalar union. Unused fields must retain their defaults.</summary>
public sealed record UiValue(
    UiValueKind Kind,
    bool Boolean = false,
    int Integer = 0,
    double Number = 0,
    string Text = "")
{
    public static UiValue FromBoolean(bool value) => new(UiValueKind.Boolean, Boolean: value);
    public static UiValue FromInt32(int value) => new(UiValueKind.Int32, Integer: value);
    public static UiValue FromNumber(double value) => new(UiValueKind.Number, Number: value);
    public static UiValue FromString(string value) => new(UiValueKind.String, Text: value);
}
