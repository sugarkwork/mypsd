using System;
using System.Collections.Generic;

namespace MyPsdWriter;

public static class PsdLayerFactory
{
    /// <summary>
    /// Build one layer from sparse colored regions over a fully transparent background.
    /// </summary>
    public static PsdLayer CreateFromRegions(
        string name,
        int left,
        int top,
        int width,
        int height,
        IEnumerable<TransparentRegion> regions,
        byte opacity = 255,
        bool visible = true)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var pixels = new byte[width * height * 4];

        foreach (var region in regions)
        {
            if (region.Width <= 0 || region.Height <= 0)
                continue;

            var x0 = Math.Max(region.X, 0);
            var y0 = Math.Max(region.Y, 0);
            var x1 = Math.Min(region.X + region.Width, width);
            var y1 = Math.Min(region.Y + region.Height, height);

            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var index = ((y * width) + x) * 4;
                    pixels[index] = region.R;
                    pixels[index + 1] = region.G;
                    pixels[index + 2] = region.B;
                    pixels[index + 3] = region.A;
                }
            }
        }

        return new PsdLayer
        {
            Name = name,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            PixelsRgba = pixels,
            Opacity = opacity,
            Visible = visible
        };
    }
}
