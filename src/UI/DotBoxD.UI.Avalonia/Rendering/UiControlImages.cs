using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia;

internal sealed class UiControlImages : IDisposable
{
    private readonly Dictionary<int, WriteableBitmap> _images = [];

    public void Materialize(UiPackage package, UiResourceCatalog resources)
    {
        foreach (var resource in package.Resources)
        {
            var image = resources.GetImage(resource.Handle);
            var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
                PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            _images.Add(resource.Id, bitmap);
            using var pixels = bitmap.Lock();
            var data = image.Rgba.ToArray();
            var stride = image.Width * 4;
            for (var row = 0; row < image.Height; row++)
            { Marshal.Copy(data, row * stride, pixels.Address + row * pixels.RowBytes, stride); }
        }
    }

    public void Apply(Image control, int resource)
    {
        control.Source = _images[resource];
        control.Stretch = Stretch.None;
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
        { image.Dispose(); }
        _images.Clear();
    }
}
