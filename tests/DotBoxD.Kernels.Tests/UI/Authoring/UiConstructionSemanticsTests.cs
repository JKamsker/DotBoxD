namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiConstructionSemanticsTests
{
    [Theory]
    [InlineData("public sealed class Box { public int Value { get; set; } public Box(int value) { Value = value + 1; } }", "=> new Box(value).Value;")]
    [InlineData("public sealed class Box { public int Value { get; set; } public Box() { throw new System.Exception(); } public Box(int value) { Value = value; } }", "=> new Box { Value = value }.Value;")]
    [InlineData("public sealed class Box { private int stored; public int Value { get => stored; set => stored = value + 1; } public Box(int value) { Value = value; } }", "=> new Box(value).Value;")]
    [InlineData("public sealed class Box { private int stored; public int Value { get => stored; set => stored = value + 1; } public Box() { } public Box(int value) { Value = value; } }", "=> new Box { Value = value }.Value;")]
    [InlineData("public sealed class Box { public static int Calls; public int Value { get; } public Box(int value) { Calls++; Value = value; } }", "=> new Box(value).Value;")]
    [InlineData("public sealed class Box { public int Value { get; set; } = Fail(); private static int Fail() => throw new System.Exception(); public Box(int value) { Value = value; } }", "=> new Box(value).Value;")]
    [InlineData("public sealed record Box(int Value) { public int Dangerous => 10 / Value; }", "{ var box = new Box(value); return value; }")]
    [InlineData("public sealed class Box { public int Value { get; set; } public Box(int value) { Value = value + 1; } }", "=> Make(value).Value;")]
    [InlineData("public sealed class Box { public int Value { get; set; } public int Other { get; set; } public Box(int value, int other = 7) { Value = other; Other = value; } }", "=> new Box(value, 7).Value;")]
    [InlineData("public sealed class Box { public int Value { get; set; } public int Other { get; set; } public Box(int value, int other = 7) { Value = other; Other = value; } }", "=> Make(value).Value;")]
    public void Nontrivial_DTO_construction_and_accessors_fail_closed(string type, string body)
        => UiConstructionFixture.AssertUnsupported(type, body);

    [Theory]
    [InlineData("public sealed record Box(int Value);", "=> new Box(value).Value;", ExecutionMode.Interpreted)]
    [InlineData("public sealed record Box(int Value);", "=> new Box(value).Value;", ExecutionMode.Compiled)]
    [InlineData("public readonly record struct Box(int Value);", "=> new Box(value).Value;", ExecutionMode.Interpreted)]
    [InlineData("public readonly record struct Box(int Value);", "=> new Box(value).Value;", ExecutionMode.Compiled)]
    [InlineData("public sealed class Box { public int Value { get; } public Box(int value) { Value = value; } }", "=> new Box(value).Value;", ExecutionMode.Interpreted)]
    [InlineData("public sealed class Box { public int Value { get; } public Box(int value) { Value = value; } }", "=> new Box(value).Value;", ExecutionMode.Compiled)]
    [InlineData("public sealed class Box { public int Value { get; set; } public Box() { } public Box(int value) { Value = value; } }", "=> new Box { Value = value }.Value;", ExecutionMode.Interpreted)]
    [InlineData("public sealed class Box { public int Value { get; set; } public Box() { } public Box(int value) { Value = value; } }", "=> new Box { Value = value }.Value;", ExecutionMode.Compiled)]
    [InlineData("public sealed class Box { public int Value { get; set; } public int Other { get; set; } public Box(int value, int other = 7) { Other = other; Value = value; } }", "=> new Box(value, 7).Value;", ExecutionMode.Interpreted)]
    [InlineData("public sealed class Box { public int Value { get; set; } public int Other { get; set; } public Box(int value, int other = 7) { Other = other; Value = value; } }", "=> new Box(value, 7).Value;", ExecutionMode.Compiled)]
    [InlineData("public sealed class Box { public int Value { get; set; } public int Other { get; set; } public Box(int value, int other = 7) { Other = other; Value = value; } }", "=> new Box(value, 7).Other;", ExecutionMode.Interpreted)]
    [InlineData("public sealed class Box { public int Value { get; set; } public int Other { get; set; } public Box(int value, int other = 7) { Other = other; Value = value; } }", "=> new Box(value, 7).Other;", ExecutionMode.Compiled)]
    public Task Plain_stored_DTOs_preserve_native_execution_in_both_modes(string type, string body, ExecutionMode mode)
        => UiConstructionFixture.AssertMatchesNative(type, body, mode);
}
