using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Core.Contracts;

public sealed class SandboxDiagnosticContractTests
{
    [Fact]
    public void Sandbox_diagnostic_rejects_null_code_and_message()
    {
        var code = Assert.Throws<ArgumentNullException>(
            () => new SandboxDiagnostic(null!, "message"));
        var message = Assert.Throws<ArgumentNullException>(
            () => new SandboxDiagnostic("E-TEST", null!));

        Assert.Equal("Code", code.ParamName);
        Assert.Equal("Message", message.ParamName);
    }

    [Fact]
    public void Sandbox_diagnostic_rejects_null_code_and_message_updates()
    {
        var diagnostic = new SandboxDiagnostic("E-TEST", "message");

        var code = Assert.Throws<ArgumentNullException>(
            () => diagnostic with { Code = null! });
        var message = Assert.Throws<ArgumentNullException>(
            () => diagnostic with { Message = null! });

        Assert.Equal("Code", code.ParamName);
        Assert.Equal("Message", message.ParamName);
    }

    [Fact]
    public void Sandbox_diagnostic_rejects_blank_code_and_message()
    {
        var code = Assert.ThrowsAny<ArgumentException>(
            () => new SandboxDiagnostic("", "message"));
        var message = Assert.ThrowsAny<ArgumentException>(
            () => new SandboxDiagnostic("E-TEST", " "));

        Assert.Equal("Code", code.ParamName);
        Assert.Equal("Message", message.ParamName);
    }

    [Fact]
    public void Sandbox_diagnostic_rejects_blank_code_and_message_updates()
    {
        var diagnostic = new SandboxDiagnostic("E-TEST", "message");

        var code = Assert.ThrowsAny<ArgumentException>(
            () => diagnostic with { Code = "" });
        var message = Assert.ThrowsAny<ArgumentException>(
            () => diagnostic with { Message = " " });

        Assert.Equal("Code", code.ParamName);
        Assert.Equal("Message", message.ParamName);
    }

    [Fact]
    public void Sandbox_diagnostic_rejects_undefined_severity()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new SandboxDiagnostic("E-TEST", "message", (DiagnosticSeverity)999));

        Assert.Equal("Severity", exception.ParamName);
    }

    [Fact]
    public void Source_span_rejects_negative_coordinates()
    {
        var line = Assert.ThrowsAny<ArgumentException>(() => new SourceSpan(-1, 0));
        var column = Assert.ThrowsAny<ArgumentException>(() => new SourceSpan(0, -1));

        Assert.Equal("Line", line.ParamName);
        Assert.Equal("Column", column.ParamName);
    }

    [Fact]
    public void Source_span_rejects_negative_coordinate_updates()
    {
        var span = new SourceSpan(1, 1);

        var line = Assert.ThrowsAny<ArgumentException>(() => span with { Line = -1 });
        var column = Assert.ThrowsAny<ArgumentException>(() => span with { Column = -1 });

        Assert.Equal("Line", line.ParamName);
        Assert.Equal("Column", column.ParamName);
    }

    [Fact]
    public void Validation_exception_rejects_null_diagnostics_boundary()
    {
        var collection = Assert.Throws<ArgumentNullException>(
            () => new SandboxValidationException(null!));
        var element = Assert.ThrowsAny<ArgumentException>(
            () => new SandboxValidationException([null!]));

        Assert.Equal("diagnostics", collection.ParamName);
        Assert.Equal("diagnostics", element.ParamName);
    }
}
