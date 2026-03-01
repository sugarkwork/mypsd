using System.Collections.Generic;

namespace MyPsdWriter;

public sealed class PsdDocument
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public IList<PsdLayer> Layers { get; } = new List<PsdLayer>();
}
