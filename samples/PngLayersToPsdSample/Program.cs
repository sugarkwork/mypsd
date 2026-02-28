using MyPsdWriter;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length != 4)
{
    Console.WriteLine("Usage: PngLayersToPsdSample <layer1.png> <layer2.png> <layer3.png> <output.psd>");
    return;
}

var inputPngs = args.Take(3).ToArray();
var outputPsd = args[3];

var images = new List<Image<Rgba32>>();
try
{
    foreach (var path in inputPngs)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"PNG file not found: {path}");

        images.Add(Image.Load<Rgba32>(path));
    }

    var width = images[0].Width;
    var height = images[0].Height;

    if (images.Any(x => x.Width != width || x.Height != height))
        throw new InvalidOperationException("All PNG files must have the same width and height.");

    var doc = new PsdDocument
    {
        Width = width,
        Height = height
    };

    for (var i = 0; i < images.Count; i++)
    {
        var layerName = Path.GetFileNameWithoutExtension(inputPngs[i]);
        doc.Layers.Add(new PsdLayer
        {
            Name = layerName,
            Left = 0,
            Top = 0,
            Width = width,
            Height = height,
            PixelsRgba = ToRgbaBytes(images[i])
        });
    }

    await using var fs = File.Create(outputPsd);
    PsdWriter.Write(doc, fs);

    Console.WriteLine($"PSD written: {outputPsd}");
}
finally
{
    foreach (var image in images)
        image.Dispose();
}

static byte[] ToRgbaBytes(Image<Rgba32> image)
{
    var bytes = new byte[image.Width * image.Height * 4];

    image.ProcessPixelRows(accessor =>
    {
        for (var y = 0; y < accessor.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (var x = 0; x < row.Length; x++)
            {
                var pixel = row[x];
                var idx = ((y * image.Width) + x) * 4;
                bytes[idx] = pixel.R;
                bytes[idx + 1] = pixel.G;
                bytes[idx + 2] = pixel.B;
                bytes[idx + 3] = pixel.A;
            }
        }
    });

    return bytes;
}
