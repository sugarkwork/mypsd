using System;

namespace MyPsdWriter;

public sealed class PsdLayer
{
    public required string Name { get; init; }
    public required int Left { get; init; }
    public required int Top { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>
    /// RGBA pixels in row-major order. Length must be Width * Height * 4.
    /// </summary>
    public required byte[] PixelsRgba { get; init; }

    public byte Opacity { get; init; } = 255;
    public bool Visible { get; init; } = true;

    public void Validate(int canvasWidth, int canvasHeight)
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Layer name is required.", nameof(Name));
        if (Width <= 0 || Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Layer width and height must be > 0.");
        if (Left < 0 || Top < 0 || Left + Width > canvasWidth || Top + Height > canvasHeight)
            throw new ArgumentOutOfRangeException(nameof(Left), "Layer rectangle must stay inside the canvas.");

        var expected = Width * Height * 4;
        if (PixelsRgba.Length != expected)
            throw new ArgumentException($"PixelsRgba length must be {expected}.", nameof(PixelsRgba));
    }
}
