using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.Coverage.Core;

[DotBoxDService(Name = "decorated-wire")]
internal interface IDecoratedService
{
    [DotBoxDMethod(Name = "WireMethod")]
    Task RenamedAsync(CancellationToken ct = default);
}
