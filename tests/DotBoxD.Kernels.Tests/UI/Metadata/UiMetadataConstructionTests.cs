namespace DotBoxD.Kernels.Tests.UI.Metadata;

public sealed class UiMetadataConstructionTests
{
    [Theory]
    [InlineData(UiMetadataFixture.Getter, "=> new Box { Value = value }.Value;", true)]
    [InlineData(UiMetadataFixture.Setter, "=> new Box { Value = value }.Value;", true)]
    [InlineData(UiMetadataFixture.Getter, "=> new Box().Value;", true)]
    [InlineData(UiMetadataFixture.Getter, "=> Make(value).Value;", true)]
    [InlineData(UiMetadataFixture.Getter, "=> new Container { Box = new Box { Value = value } }.Box.Value;", true)]
    [InlineData(UiMetadataFixture.Getter, "=> new GenericContainer<Box> { Item = new Box { Value = value } }.Item.Value;", true)]
    [InlineData(UiMetadataFixture.Overlay, "=> new Box { First = value, Second = value + 1 }.First;", true)]
    [InlineData(UiMetadataFixture.Overlay, "=> new Box { First = value, Second = value + 1 }.First;", false)]
    [InlineData(UiMetadataFixture.Fields, "=> new Box { Value = value }.Value;", true)]
    public void Opaque_or_overlapping_DTO_storage_fails_closed_during_generation(string type, string body, bool metadata)
        => UiMetadataFixture.AssertUnsupported(type, body, metadata);

    [Theory]
    [InlineData("=> new Box { Value = value }.Value;", ExecutionMode.Interpreted)]
    [InlineData("=> new Box { Value = value }.Value;", ExecutionMode.Compiled)]
    [InlineData("=> new Container { Box = new Box { Value = value } }.Box.Value;", ExecutionMode.Interpreted)]
    [InlineData("=> new Container { Box = new Box { Value = value } }.Box.Value;", ExecutionMode.Compiled)]
    [InlineData("=> new GenericContainer<Box> { Item = new Box { Value = value } }.Item.Value;", ExecutionMode.Interpreted)]
    [InlineData("=> new GenericContainer<Box> { Item = new Box { Value = value } }.Item.Value;", ExecutionMode.Compiled)]
    public Task Source_visible_fields_preserve_execution_in_both_kernel_modes(string body, ExecutionMode mode)
        => UiMetadataFixture.AssertMatchesNative(UiMetadataFixture.Fields, body, mode, metadata: false);
}
