namespace MyPsdWriter;

public readonly record struct TransparentRegion(int X, int Y, int Width, int Height, byte R, byte G, byte B, byte A);
