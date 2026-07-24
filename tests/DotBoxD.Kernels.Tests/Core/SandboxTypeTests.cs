using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxTypeTests
{
    [Fact]
    public void Type_factories_reject_null_operands()
    {
        Assert.ThrowsAny<ArgumentException>(() => SandboxType.Scalar(null!));
        Assert.ThrowsAny<ArgumentException>(() => SandboxType.List(null!));
        Assert.ThrowsAny<ArgumentException>(() => SandboxType.Map(null!, SandboxType.I32));
        Assert.ThrowsAny<ArgumentException>(() => SandboxType.Map(SandboxType.String, null!));
        Assert.ThrowsAny<ArgumentException>(() => SandboxType.Record([null!]));
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("System.String")]
    [InlineData("Microsoft.Extensions.Logging.ILogger")]
    public void IsKnown_rejects_forbidden_scalar_names(string name)
    {
        Assert.False(SandboxType.Scalar(name).IsKnown());
    }

    [Fact]
    public void IsKnown_rejects_forbidden_names_nested_in_structural_types()
    {
        var types = new[] {
            SandboxType.List(SandboxType.Scalar("System.String")),
            SandboxType.Map(SandboxType.String, SandboxType.Scalar("System.String")),
            SandboxType.Record([SandboxType.I32, SandboxType.Scalar("System.String")])
        };

        foreach (var type in types)
        {
            Assert.False(type.IsKnown());
        }
    }

    [Fact]
    public void IsKnown_with_declared_opaque_ids_rejects_forbidden_nested_types()
    {
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal) { "PlayerId" };
        var allowed = SandboxType.Map(
            SandboxType.Scalar("PlayerId"),
            SandboxType.List(SandboxType.Scalar("PlayerId")));
        var forbidden = SandboxType.Record([
            SandboxType.Scalar("PlayerId"),
            SandboxType.Scalar("System.String")
        ]);

        Assert.True(allowed.IsKnown(declaredOpaqueIds));
        Assert.False(forbidden.IsKnown(declaredOpaqueIds));
    }

    [Fact]
    public void IsKnownBuiltIn_rejects_forbidden_names_nested_in_structural_types()
    {
        var type = SandboxType.Record([
            SandboxType.String,
            SandboxType.Scalar("System.String")
        ]);

        Assert.False(type.IsKnownBuiltIn());
    }

    [Fact]
    public void IsKnown_accepts_the_complete_builtin_closed_set()
    {
        var builtInScalars = new[] {
            SandboxType.Unit,
            SandboxType.Bool,
            SandboxType.I32,
            SandboxType.I64,
            SandboxType.F64,
            SandboxType.String,
            SandboxType.Guid,
            SandboxType.SandboxPath,
            SandboxType.SandboxUri
        };
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in builtInScalars)
        {
            Assert.True(type.IsKnown());
            Assert.True(type.IsKnown(declaredOpaqueIds));
            Assert.True(type.IsKnownBuiltIn());
        }

        var nested = SandboxType.Record([
            SandboxType.List(SandboxType.I64),
            SandboxType.Map(SandboxType.String, SandboxType.SandboxUri)
        ]);
        Assert.True(nested.IsKnown());
        Assert.True(nested.IsKnown(declaredOpaqueIds));
        Assert.True(nested.IsKnownBuiltIn());
    }

    [Fact]
    public void IsKnown_keeps_open_and_declared_opaque_id_policies_distinct()
    {
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal) { "PlayerId" };
        var declared = SandboxType.List(SandboxType.Scalar("PlayerId"));
        var undeclared = SandboxType.List(SandboxType.Scalar("EnemyId"));

        Assert.True(declared.IsKnown());
        Assert.True(declared.IsKnown(declaredOpaqueIds));
        Assert.False(declared.IsKnownBuiltIn());

        Assert.True(undeclared.IsKnown());
        Assert.False(undeclared.IsKnown(declaredOpaqueIds));
        Assert.False(undeclared.IsKnownBuiltIn());
    }

    [Fact]
    public void IsKnown_rejects_forbidden_names_in_every_structural_position()
    {
        var declaredForbiddenNames = new HashSet<string>(StringComparer.Ordinal) {
            "Object",
            "System.String"
        };
        var types = new[] {
            SandboxType.Map(SandboxType.Scalar("Object"), SandboxType.I32),
            SandboxType.List(SandboxType.Scalar("System.String")),
            new SandboxType("Object", [SandboxType.I32])
        };

        foreach (var type in types)
        {
            Assert.False(type.IsKnown());
            Assert.False(type.IsKnown(declaredForbiddenNames));
            Assert.False(type.IsKnownBuiltIn());
        }
    }

    [Fact]
    public void IsKnown_rejects_unknown_non_scalar_shapes()
    {
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal) { "PlayerId" };
        var types = new[] {
            new SandboxType("Tuple", [SandboxType.I32]),
            new SandboxType("PlayerId", [SandboxType.I32]),
            new SandboxType("I32", [SandboxType.I32])
        };

        foreach (var type in types)
        {
            Assert.False(type.IsKnown());
            Assert.False(type.IsKnown(declaredOpaqueIds));
            Assert.False(type.IsKnownBuiltIn());
        }
    }

    [Fact]
    public void IsKnown_accepts_the_depth_limit_and_rejects_the_next_level()
    {
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal);
        var atLimit = WrapInLists(SandboxType.I32, count: 2);
        var beyondLimit = WrapInLists(atLimit, count: 1);

        Assert.True(atLimit.IsKnown(maxDepth: 2));
        Assert.True(atLimit.IsKnown(declaredOpaqueIds, maxDepth: 2));
        Assert.True(atLimit.IsKnownBuiltIn(maxDepth: 2));

        Assert.False(beyondLimit.IsKnown(maxDepth: 2));
        Assert.False(beyondLimit.IsKnown(declaredOpaqueIds, maxDepth: 2));
        Assert.False(beyondLimit.IsKnownBuiltIn(maxDepth: 2));
    }

    [Fact]
    public void IsForbidden_fails_closed_for_types_beyond_known_depth_limit()
    {
        var type = SandboxType.Scalar("PlayerId");
        for (var i = 0; i < 10; i++)
        {
            type = SandboxType.List(type);
        }

        Assert.True(type.IsForbidden());
    }

    private static SandboxType WrapInLists(SandboxType type, int count)
    {
        for (var i = 0; i < count; i++)
        {
            type = SandboxType.List(type);
        }

        return type;
    }
}
