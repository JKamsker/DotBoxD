using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiPackageValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Null_items_properties_report_validation_errors_for_objects_and_wire(bool wire)
    {
        var policy = new UiPolicy();
        var package = new UiPackage(1, 1, [new UiNode(1, UiPrimitive.Items, [2], []),
            new UiNode(2, UiPrimitive.Text, [], [])], [], [], [], []);
        if (wire)
        {
            var json = UiPackageJson.Export(package, policy).Replace("\"properties\":[]", "\"properties\":[null]", StringComparison.Ordinal);
            Assert.Throws<UiValidationException>(() => UiPackageJson.Import(json, policy));
        }
        else
        {
            var malformed = package with { Nodes = package.Nodes.SetItem(0, package.Nodes[0] with { Properties = [null!] }) };
            Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(malformed, policy));
        }
    }

    public static IEnumerable<object[]> MalformedPackages()
    {
        var p = UiTestFixture.Counter();
        yield return [p with { FormatVersion = 2 }];
        yield return [p with { RootNodeId = 999 }];
        yield return [p with { Nodes = default }];
        yield return [p with { Nodes = [.. p.Nodes, p.Nodes[1]] }];
        yield return [p with { Nodes = [p.Nodes[0] with { Children = [1] }, .. p.Nodes.Skip(1)] }];
        yield return [p with { Nodes = [p.Nodes[0] with { Children = [2, 2, 3, 4, 5, 6] }, .. p.Nodes.Skip(1)] }];
        yield return [p with { Nodes = [p.Nodes[0] with { Primitive = (UiPrimitive)999 }, .. p.Nodes.Skip(1)] }];
        yield return [p with { Nodes = [p.Nodes[0] with { Children = [999] }, .. p.Nodes.Skip(1)] }];
        yield return [p with { Nodes = [p.Nodes[0] with { Children = [2] }, .. p.Nodes.Skip(1)] }];
        yield return [p with { State = [p.State[0] with { Id = -1 }, .. p.State.Skip(1)] }];
        yield return [p with { State = [p.State[0] with { InitialValue = new UiValue((UiValueKind)999) }, .. p.State.Skip(1)] }];
        yield return [p with { Kernels = [p.Kernels[0] with { InputSlotId = 99 }, p.Kernels[1]] }];
        yield return [p with { RemoteEndpoints = [7, 7] }];
        yield return [p with { Events = [p.Events[0] with { Target = (UiEventTarget)999 }, p.Events[1]] }];
        yield return [p with { Events = [p.Events[0] with { NodeId = 2 }, p.Events[1]] }];
        yield return [p with { Events = [p.Events[0] with { RemoteEndpointId = 7 }, p.Events[1]] }];
        yield return [p with { Events = [p.Events[0], p.Events[1] with { RemoteEndpointId = 99 }] }];
        yield return [p with { Events = [p.Events[0], p.Events[0] with { Id = 3 }] }];
    }

    [Theory]
    [MemberData(nameof(MalformedPackages))]
    public void Malformed_graphs_ids_sources_and_versions_fail_closed(UiPackage package)
        => Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(package, new UiPolicy()));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Unsupported_or_ambiguous_properties_are_rejected(int scenario)
    {
        var p = UiTestFixture.Counter();
        var property = scenario switch
        {
            0 => new UiProperty((UiPropertyId)999, UiValue.FromString("bad")),
            1 => new UiProperty(UiPropertyId.Text, UiValue.FromBoolean(true)),
            2 => new UiProperty(UiPropertyId.Text),
            3 => new UiProperty(UiPropertyId.Text, UiValue.FromString("bad"), StateSlotId: 2),
            4 => new UiProperty(UiPropertyId.Text, StateSlotId: 2, TwoWay: true),
            _ => new UiProperty(UiPropertyId.Text, BindingKernelId: 999)
        };
        p = p with { Nodes = [p.Nodes[0], p.Nodes[1] with { Properties = [property] }, .. p.Nodes.Skip(2)] };
        Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(p, new UiPolicy()));
    }

    [Fact]
    public void Structural_quotas_are_independent_and_host_selected()
    {
        var p = UiTestFixture.Counter();
        foreach (var policy in new[]
        {
            new UiPolicy { MaxNodes = 2 }, new UiPolicy { MaxDepth = 1 }, new UiPolicy { MaxChildren = 2 },
            new UiPolicy { MaxStateSlots = 2 }, new UiPolicy { MaxStateBytes = 16 },
            new UiPolicy { MaxKernels = 1 }, new UiPolicy { MaxKernelBytes = 10 },
            new UiPolicy { MaxEvents = 1 }, new UiPolicy { MaxStringLength = 2 },
            new UiPolicy { AllowedPrimitives = [UiPrimitive.Stack] }
        })
        {
            Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(p, policy));
        }
    }

    [Fact]
    public void Scalars_reject_hidden_fields_nonfinite_numbers_and_invalid_unicode()
    {
        foreach (var value in new[]
        {
            UiValue.FromNumber(double.NaN), UiValue.FromNumber(double.PositiveInfinity),
            UiValue.FromString("\ud800"), UiValue.FromString("\udfff"),
            new UiValue(UiValueKind.Int32, Text: "hidden"),
            new UiValue(UiValueKind.String, Boolean: true),
            UiValue.FromString(null!)
        })
        {
            Assert.Throws<UiValidationException>(() => UiValueValidator.Validate(value, new UiPolicy()));
        }
    }

    [Fact]
    public async Task Patch_string_slot_and_total_state_quotas_reject_without_mutation()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var policy = new UiPolicy { MaxStringLength = 32, MaxStateBytes = 40, MaxPatchSlots = 2 };
        await using var session = await UiTestFixture.Host(sandbox, policy)
            .InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), TestUiTransport.Echo());
        foreach (var writes in new UiStateValue[][]
        {
            [new(2, UiValue.FromString(new string('x', 33)))],
            [new(2, UiValue.FromString("123456")), new(3, UiValue.FromString("123456"))],
            [new(1, UiValue.FromInt32(1)), new(2, UiValue.FromString("a")), new(3, UiValue.FromString("b"))]
        })
        {
            await Assert.ThrowsAsync<UiValidationException>(async () =>
                await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [.. writes])));
        }

        Assert.Equal(0, (await session.SnapshotAsync()).Version);
    }
}
