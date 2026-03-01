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

        // どの PNG 形式でも最終的に RGBA32 へ正規化
        using var decoded = Image.Load(path);
        images.Add(decoded.CloneAs<Rgba32>());
    }

    // キャンバスは最大サイズを採用。各レイヤーは元 PNG のサイズを保持する。
    var canvasWidth = images.Max(x => x.Width);
    var canvasHeight = images.Max(x => x.Height);

    var doc = new PsdDocument
    {
        Width = canvasWidth,
        Height = canvasHeight
    };

    for (var i = 0; i < images.Count; i++)
    {
        var image = images[i];
        var rgba = ToRgbaBytes(image);

        var expectedLength = checked(image.Width * image.Height * 4);
        if (rgba.Length != expectedLength)
            throw new InvalidOperationException(
                $"Unexpected pixel buffer length for {inputPngs[i]}. expected={expectedLength}, actual={rgba.Length}");

        var layerName = Path.GetFileNameWithoutExtension(inputPngs[i]);
        doc.Layers.Add(new PsdLayer
        {
            Name = layerName,
            Left = 0,
            Top = 0,
            Width = image.Width,
            Height = image.Height,
            PixelsRgba = rgba
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
    var bytes = new byte[checked(image.Width * image.Height * 4)];
    image.CopyPixelDataTo(bytes);
    return bytes;
}
