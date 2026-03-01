using MyPsdWriter;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;

var baseDir = Directory.GetCurrentDirectory();
var inputPngs = new[] { "baselayer.png", "addlayer.png" };

var images = new List<Image<Rgba32>>();
try
{
    foreach (var path in inputPngs)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"PNG file not found: {path}");

        using var decoded = Image.Load(path);
        images.Add(decoded.CloneAs<Rgba32>());
    }

    var canvasWidth = images.Max(x => x.Width);
    var canvasHeight = images.Max(x => x.Height);

    var methods = new[]
    {
        CompressionMethod.Raw,
        CompressionMethod.Rle,
        CompressionMethod.ZipWithoutPrediction,
        CompressionMethod.ZipWithPrediction
    };

    foreach (var method in methods)
    {
        var doc = new PsdDocument
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Compression = method
        };

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var rgba = ToRgbaBytes(image);
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

        var outputPsd = Path.Combine(baseDir, $"output_{method}.psd");
        using var fs = File.Create(outputPsd);
        PsdWriter.Write(doc, fs);

        Console.WriteLine($"PSD written with {method}: {outputPsd}");
    }
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
