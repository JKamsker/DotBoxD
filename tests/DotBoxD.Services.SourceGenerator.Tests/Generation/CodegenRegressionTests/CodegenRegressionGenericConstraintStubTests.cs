using FluentAssertions;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

/// <summary>
/// Generic service methods are unsupported, but the proxy still emits a throwing stub. When the
/// method name forces an explicit interface implementation (object members, generated field names,
/// the proxy type name), C# forbids restating constraints (CS0460) except the <c>class</c>,
/// <c>struct</c> and <c>default</c> forms that disambiguate <c>T?</c>; implicit stubs must keep
/// every constraint. Each shape must compile under both implementation styles.
/// </summary>
public class CodegenRegressionGenericConstraintStubTests
{
    private static readonly string[] s_signatures =
    [
        "void {0}<T>(T value) where T : global::System.IComparable;",
        "void {0}<T>(T value) where T : class;",
        "void {0}<T>(T value) where T : struct;",
        "void {0}<T>(T value) where T : new();",
        "void {0}<T>(T value) where T : notnull;",
        "void {0}<T>(T value) where T : unmanaged;",
        "void {0}<T>(T value) where T : global::System.IComparable, new();",
        "void {0}<T>(T? value);",
        "void {0}<T>(T? value) where T : class;",
        "void {0}<T>(T? value) where T : class?;",
        "void {0}<T>(T? value) where T : struct;",
        "void {0}<T>(T? value) where T : unmanaged;",
        "void {0}<T>(T? value) where T : notnull;",
        "void {0}<T>(T? value) where T : global::System.IComparable;",
        "void {0}<T>(T? value) where T : global::System.IO.Stream;",
        "void {0}<T>(T? value) where T : global::System.Enum;",
        "T? {0}<T>(T value) where T : notnull, new();",
        "void {0}<T>(global::System.Collections.Generic.List<T?> value) where T : global::System.IComparable;",
        "void {0}<T>(T?[] value) where T : global::System.IO.Stream;",
        "void {0}<T, U>(T? first, U? second, U third) where T : class where U : global::System.IComparable;",
    ];

    // Echo is implemented implicitly; the other names require explicit implementation.
    private static readonly string[] s_methodNames = ["Echo", "Equals", "GetHashCode", "_invoker", "ConstraintsProxy"];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var signature in s_signatures)
        {
            foreach (var name in s_methodNames)
            {
                data.Add(name, signature);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GenericStub_WithConstraints_CompilesForImplicitAndExplicitImplementations(
        string methodName,
        string signatureFormat)
    {
        var source = $$"""
            #nullable enable
            using DotBoxD.Services.Attributes;

            namespace Regress.GenericConstraintStubs
            {
                [RpcService]
                public interface IConstraints
                {
                    {{string.Format(System.Globalization.CultureInfo.InvariantCulture, signatureFormat, methodName)}}
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("generic service methods are not supported"));
    }

    [Fact]
    public void ExplicitGenericStub_OmitsInheritedConstraints_AndImplicitStubKeepsThem()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.GenericExplicitConstraints
            {
                [RpcService]
                public interface IFoo
                {
                    void Equals<T>(T value) where T : System.IComparable;

                    void Echo<T>(T value) where T : System.IComparable;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = ProxySource(runResult, "Regress.GenericExplicitConstraints", "IFoo");
        proxy.Should().Contain("void global::Regress.GenericExplicitConstraints.IFoo.Equals<T>(T value)" + Environment.NewLine);
        proxy.Should().Contain("public void Echo<T>(T value) where T : global::System.IComparable");
    }

    [Fact]
    public void ExplicitGenericStub_WithNullableTypeParameter_EmitsOnlyDisambiguatingConstraint()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;

            namespace Regress.GenericExplicitNullable
            {
                [RpcService]
                public interface IFoo
                {
                    void Equals<T, U, V>(T? first, U? second, V? third)
                        where T : class, System.IDisposable, new()
                        where U : notnull, System.IComparable
                        where V : struct;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = ProxySource(runResult, "Regress.GenericExplicitNullable", "IFoo");
        proxy.Should().Contain(
            "IFoo.Equals<T, U, V>(T? first, U? second, V? third) where T : class where U : default" +
            Environment.NewLine);
    }

    [Fact]
    public void GenericStub_InheritedFromTwoBases_EmitsCompilingExplicitImplementations()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;

            namespace Regress.GenericExplicitInherited
            {
                public interface ILeft
                {
                    void Equals<T>(T? value) where T : notnull, System.IComparable;
                }

                public interface IRight
                {
                    void Equals<U>(U? value) where U : notnull, System.IComparable;
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = ProxySource(runResult, "Regress.GenericExplicitInherited", "ICombined");
        proxy.Should().Contain("ILeft.Equals<T>(T? value) where T : default" + Environment.NewLine);
        proxy.Should().Contain("IRight.Equals<T>(T? value) where T : default" + Environment.NewLine);
    }

    [Fact]
    public void ExplicitGenericStub_WithRefStructAntiConstraint_Compiles()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.GenericExplicitRefStruct
            {
                [RpcService]
                public interface IFoo
                {
                    T Equals<T>(T value) where T : allows ref struct;
                }
            }
            """;

        var (final, runResult) = RunWithPreviewByRefLikeGenerics(source);
        AssertCompiles(final);

        var proxy = ProxySource(runResult, "Regress.GenericExplicitRefStruct", "IFoo");
        proxy.Should().Contain("T global::Regress.GenericExplicitRefStruct.IFoo.Equals<T>(T value)" + Environment.NewLine);
    }

    private static string ProxySource(
        Microsoft.CodeAnalysis.GeneratorDriverRunResult runResult,
        string @namespace,
        string interfaceName)
        => runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                @namespace, interfaceName, GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
}
