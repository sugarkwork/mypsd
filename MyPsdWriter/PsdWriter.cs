using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MyPsdWriter;

public static class PsdWriter
{
    public static void Write(PsdDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        if (document.Width <= 0 || document.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(document), "Canvas size must be > 0.");

        foreach (var layer in document.Layers)
            layer.Validate(document.Width, document.Height);

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        WriteHeader(writer, document.Width, document.Height, channels: 3);
        WriteColorModeData(writer);
        WriteImageResources(writer);
        WriteLayerAndMaskInfo(writer, document);
        WriteCompositeImageData(writer, document);
    }

    private static void WriteHeader(BinaryWriter writer, int width, int height, short channels)
    {
        writer.Write(Encoding.ASCII.GetBytes("8BPS"));
        WriteInt16BE(writer, 1);
        writer.Write(new byte[6]);
        WriteInt16BE(writer, channels);
        WriteInt32BE(writer, height);
        WriteInt32BE(writer, width);
        WriteInt16BE(writer, 8); // depth
        WriteInt16BE(writer, 3); // RGB color mode
    }

    private static void WriteColorModeData(BinaryWriter writer)
        => WriteInt32BE(writer, 0);

    private static void WriteImageResources(BinaryWriter writer)
        => WriteInt32BE(writer, 0);

    private static void WriteLayerAndMaskInfo(BinaryWriter writer, PsdDocument document)
    {
        using var section = new MemoryStream();
        using (var sectionWriter = new BinaryWriter(section, Encoding.ASCII, leaveOpen: true))
        {
            WriteLayerInfo(sectionWriter, document);
            WriteInt32BE(sectionWriter, 0); // global layer mask info length
        }

        WriteInt32BE(writer, checked((int)section.Length));
        writer.Write(section.GetBuffer(), 0, checked((int)section.Length));
    }

    private static void WriteLayerInfo(BinaryWriter writer, PsdDocument document)
    {
        using var layerInfo = new MemoryStream();
        using var li = new BinaryWriter(layerInfo, Encoding.ASCII, leaveOpen: true);

        var layers = document.Layers.ToList();
        WriteInt16BE(li, checked((short)layers.Count));

        var channelDataBlocks = new List<byte[]>(layers.Count * 4);

        foreach (var layer in layers)
        {
            WriteLayerRecord(li, layer, channelDataBlocks);
        }

        foreach (var block in channelDataBlocks)
            li.Write(block);

        if ((layerInfo.Length & 1) == 1)
            li.Write((byte)0);

        WriteInt32BE(writer, checked((int)layerInfo.Length));
        writer.Write(layerInfo.GetBuffer(), 0, checked((int)layerInfo.Length));
    }

    private static void WriteLayerRecord(BinaryWriter writer, PsdLayer layer, List<byte[]> channelDataBlocks)
    {
        WriteInt32BE(writer, layer.Top);
        WriteInt32BE(writer, layer.Left);
        WriteInt32BE(writer, layer.Top + layer.Height);
        WriteInt32BE(writer, layer.Left + layer.Width);

        WriteInt16BE(writer, 4); // A, R, G, B

        var alpha = ExtractChannel(layer, 3);
        var red = ExtractChannel(layer, 0);
        var green = ExtractChannel(layer, 1);
        var blue = ExtractChannel(layer, 2);

        var channelMap = new (short Id, byte[] Data)[]
        {
            (-1, alpha),
            (0, red),
            (1, green),
            (2, blue)
        };

        foreach (var (id, raw) in channelMap)
        {
            var block = BuildRleChannelBlock(raw, layer.Width, layer.Height);
            WriteInt16BE(writer, id);
            WriteInt32BE(writer, block.Length);
            channelDataBlocks.Add(block);
        }

        writer.Write(Encoding.ASCII.GetBytes("8BIM"));
        writer.Write(Encoding.ASCII.GetBytes("norm"));
        writer.Write(layer.Opacity);
        writer.Write((byte)0); // clipping

        byte flags = 0;
        if (!layer.Visible)
            flags |= 0b0000_0010;
        writer.Write(flags);
        writer.Write((byte)0);

        using var extra = new MemoryStream();
        using (var ew = new BinaryWriter(extra, Encoding.ASCII, leaveOpen: true))
        {
            WriteInt32BE(ew, 0); // layer mask data length
            WriteInt32BE(ew, 0); // layer blending ranges length
            WritePascalString(ew, layer.Name, 4);
        }

        WriteInt32BE(writer, checked((int)extra.Length));
        writer.Write(extra.GetBuffer(), 0, checked((int)extra.Length));
    }

    private static byte[] BuildRleChannelBlock(byte[] channel, int width, int height)
    {
        var (rowLengths, compressed) = EncodeRleRows(channel, width, height);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        WriteInt16BE(bw, 1); // RLE
        foreach (var rowLength in rowLengths)
            WriteUInt16BE(bw, rowLength);

        bw.Write(compressed);
        return ms.ToArray();
    }

    private static byte[] ExtractChannel(PsdLayer layer, int component)
    {
        var totalPixels = layer.Width * layer.Height;
        var channel = new byte[totalPixels];
        var src = layer.PixelsRgba;

        for (var i = 0; i < totalPixels; i++)
            channel[i] = src[(i * 4) + component];

        return channel;
    }

    private static void WriteCompositeImageData(BinaryWriter writer, PsdDocument document)
    {
        WriteInt16BE(writer, 1); // RLE compression

        var pixels = Compose(document.Width, document.Height, document.Layers);

        // PSD header is RGB mode with 3 global channels.
        // Writing a 4th global channel in RGB mode is interpreted by Photoshop
        // as an extra alpha channel (mask), which appears as a colored overlay.
        WritePlanarChannelRle(writer, pixels, document.Width, document.Height, 0); // R
        WritePlanarChannelRle(writer, pixels, document.Width, document.Height, 1); // G
        WritePlanarChannelRle(writer, pixels, document.Width, document.Height, 2); // B
    }

    private static byte[] Compose(int width, int height, IEnumerable<PsdLayer> layers)
    {
        var output = new byte[width * height * 4];

        foreach (var layer in layers)
        {
            if (!layer.Visible)
                continue;

            for (var ly = 0; ly < layer.Height; ly++)
            {
                for (var lx = 0; lx < layer.Width; lx++)
                {
                    var dx = layer.Left + lx;
                    var dy = layer.Top + ly;
                    var destIndex = ((dy * width) + dx) * 4;
                    var srcIndex = ((ly * layer.Width) + lx) * 4;

                    var srcA = layer.PixelsRgba[srcIndex + 3] / 255f * (layer.Opacity / 255f);
                    if (srcA <= 0f)
                        continue;

                    var dstA = output[destIndex + 3] / 255f;
                    var outA = srcA + dstA * (1f - srcA);

                    BlendChannel(layer.PixelsRgba[srcIndex], output, destIndex, outA, srcA, dstA, 0);
                    BlendChannel(layer.PixelsRgba[srcIndex + 1], output, destIndex, outA, srcA, dstA, 1);
                    BlendChannel(layer.PixelsRgba[srcIndex + 2], output, destIndex, outA, srcA, dstA, 2);
                    output[destIndex + 3] = (byte)Math.Clamp((int)MathF.Round(outA * 255f), 0, 255);
                }
            }
        }

        return output;
    }

    private static void BlendChannel(byte srcChannel, byte[] output, int destIndex, float outA, float srcA, float dstA, int offset)
    {
        var dst = output[destIndex + offset];
        var value = outA == 0f
            ? 0f
            : ((srcChannel / 255f) * srcA + (dst / 255f) * dstA * (1f - srcA)) / outA;

        output[destIndex + offset] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }

    private static void WritePlanarChannelRle(BinaryWriter writer, byte[] rgba, int width, int height, int component)
    {
        var planar = ExtractPlanarChannel(rgba, component);
        var (rowLengths, compressed) = EncodeRleRows(planar, width, height);

        foreach (var rowLength in rowLengths)
            WriteUInt16BE(writer, rowLength);

        writer.Write(compressed);
    }

    private static byte[] ExtractPlanarChannel(byte[] rgba, int component)
    {
        var pixels = rgba.Length / 4;
        var result = new byte[pixels];
        for (var i = 0; i < pixels; i++)
            result[i] = rgba[(i * 4) + component];

        return result;
    }

    private static (ushort[] RowLengths, byte[] Compressed) EncodeRleRows(byte[] channel, int width, int height)
    {
        var rowLengths = new ushort[height];
        using var compressed = new MemoryStream();

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            var encodedRow = PackBitsEncode(channel.AsSpan(rowStart, width));
            if (encodedRow.Length > ushort.MaxValue)
                throw new InvalidOperationException("Encoded RLE row exceeds PSD row size limit.");

            rowLengths[y] = (ushort)encodedRow.Length;
            compressed.Write(encodedRow);
        }

        return (rowLengths, compressed.ToArray());
    }

    private static byte[] PackBitsEncode(ReadOnlySpan<byte> input)
    {
        using var ms = new MemoryStream();
        var i = 0;

        while (i < input.Length)
        {
            var runLength = 1;
            while (i + runLength < input.Length && runLength < 128 && input[i] == input[i + runLength])
                runLength++;

            if (runLength >= 3)
            {
                ms.WriteByte((byte)(257 - runLength));
                ms.WriteByte(input[i]);
                i += runLength;
                continue;
            }

            var literalStart = i;
            i += runLength;

            while (i < input.Length)
            {
                runLength = 1;
                while (i + runLength < input.Length && runLength < 128 && input[i] == input[i + runLength])
                    runLength++;

                if (runLength >= 3)
                    break;

                i += runLength;

                if (i - literalStart >= 128)
                    break;
            }

            var literalCount = i - literalStart;
            ms.WriteByte((byte)(literalCount - 1));
            ms.Write(input.Slice(literalStart, literalCount));
        }

        return ms.ToArray();
    }

    private static void WritePascalString(BinaryWriter writer, string value, int padMultiple)
    {
        var bytes = Encoding.ASCII.GetBytes(value.Length > 255 ? value[..255] : value);
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);

        var total = 1 + bytes.Length;
        var padded = ((total + padMultiple - 1) / padMultiple) * padMultiple;
        for (var i = total; i < padded; i++)
            writer.Write((byte)0);
    }

    private static void WriteInt16BE(BinaryWriter writer, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        writer.Write(buffer);
    }

    private static void WriteInt32BE(BinaryWriter writer, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        writer.Write(buffer);
    }

    private static void WriteUInt16BE(BinaryWriter writer, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        writer.Write(buffer);
    }
}
