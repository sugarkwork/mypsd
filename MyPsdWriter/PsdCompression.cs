using System;
using System.IO;
using System.IO.Compression;

namespace MyPsdWriter;

public enum CompressionMethod : short
{
    Raw = 0,
    Rle = 1,
    ZipWithoutPrediction = 2,
    ZipWithPrediction = 3
}

public static class PsdCompression
{
    public static byte[] CompressChannel(byte[] raw, int width, int height, CompressionMethod method)
    {
        return method switch
        {
            CompressionMethod.Raw => CompressRaw(raw),
            CompressionMethod.Rle => CompressRle(raw, width, height),
            CompressionMethod.ZipWithoutPrediction => CompressZip(raw, false),
            CompressionMethod.ZipWithPrediction => CompressZipPredict(raw, width, height),
            _ => throw new NotSupportedException($"Compression {method} is not supported.")
        };
    }

    private static byte[] CompressRaw(byte[] raw)
    {
        var result = new byte[2 + raw.Length];
        // 0 = Raw
        result[0] = 0;
        result[1] = 0;
        Buffer.BlockCopy(raw, 0, result, 2, raw.Length);
        return result;
    }

    private static byte[] CompressRle(byte[] raw, int width, int height)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0);
        ms.WriteByte(1); // RLE compression

        var counts = new short[height];
        var linesBuffer = new byte[height][];

        for (int y = 0; y < height; y++)
        {
            var lineData = RleEncodeLine(raw.AsSpan(y * width, width));
            // pad byte to make it even if needed by PSD spec per channel? 
            // The spec says "If the layer's size, and therefore the data, is odd, a pad byte will be inserted at the end of the row."
            // But actually we just write the compressed sequence byte lengths.
            counts[y] = (short)lineData.Length;
            linesBuffer[y] = lineData;
        }

        // Write scanline byte counts
        foreach (var count in counts)
        {
            ms.WriteByte((byte)(count >> 8));
            ms.WriteByte((byte)(count & 0xFF));
        }

        // Write scanline data
        foreach (var lineData in linesBuffer)
        {
            ms.Write(lineData);
        }

        return ms.ToArray();
    }

    private static byte[] RleEncodeLine(ReadOnlySpan<byte> line)
    {
        using var ms = new MemoryStream();
        int i = 0;
        while (i < line.Length)
        {
            // Find duplicate run
            int runLength = 1;
            while (i + runLength < line.Length && line[i] == line[i + runLength] && runLength < 128)
            {
                runLength++;
            }

            if (runLength >= 3) // Only encode as run if it's 3 or more bytes, to save space, but let's just do 2 or more
            {
                ms.WriteByte((byte)(257 - runLength)); // 256-(L-1) = 257-L
                ms.WriteByte(line[i]);
                i += runLength;
                continue;
            }

            // Find literal run
            int literalLength = 0;
            while (i + literalLength < line.Length && literalLength < 128)
            {
                bool hasDuplicate = (i + literalLength + 2 < line.Length &&
                                     line[i + literalLength] == line[i + literalLength + 1] &&
                                     line[i + literalLength] == line[i + literalLength + 2]);
                if (hasDuplicate)
                    break;
                literalLength++;
            }

            ms.WriteByte((byte)(literalLength - 1));
            for (int k = 0; k < literalLength; k++)
            {
                ms.WriteByte(line[i + k]);
            }
            i += literalLength;
        }

        return ms.ToArray();
    }

    private static byte[] CompressZip(byte[] raw, bool predict)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0);
        ms.WriteByte(predict ? (byte)3 : (byte)2); // Zip compression

        using (var zlibStream = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlibStream.Write(raw);
        }

        return ms.ToArray();
    }

    private static byte[] CompressZipPredict(byte[] raw, int width, int height)
    {
        var dataToCompress = new byte[raw.Length];
        for (int y = 0; y < height; y++)
        {
            int offset = y * width;
            dataToCompress[offset] = raw[offset];
            for (int x = 1; x < width; x++)
            {
                dataToCompress[offset + x] = unchecked((byte)(raw[offset + x] - raw[offset + x - 1]));
            }
        }
        return CompressZip(dataToCompress, true);
    }

    // For composite (global) image data
    public static byte[] CompressGlobalImageData(byte[][] channels, int width, int height, CompressionMethod method)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0);
        ms.WriteByte((byte)method);

        if (method == CompressionMethod.Raw)
        {
            foreach (var ch in channels)
                ms.Write(ch);
            return ms.ToArray();
        }

        if (method == CompressionMethod.Rle)
        {
            int channelCount = channels.Length;
            var allCounts = new short[channelCount * height];
            var allLines = new byte[channelCount * height][];

            for (int c = 0; c < channelCount; c++)
            {
                for (int y = 0; y < height; y++)
                {
                    var lineData = RleEncodeLine(channels[c].AsSpan(y * width, width));
                    allCounts[c * height + y] = (short)lineData.Length;
                    allLines[c * height + y] = lineData;
                }
            }

            foreach (var count in allCounts)
            {
                ms.WriteByte((byte)(count >> 8));
                ms.WriteByte((byte)(count & 0xFF));
            }

            foreach (var line in allLines)
            {
                ms.Write(line);
            }

            return ms.ToArray();
        }

        byte[] combinedRaw = new byte[channels.Length * width * height];
        int ptr = 0;
        
        if (method == CompressionMethod.ZipWithPrediction)
        {
            for (int c = 0; c < channels.Length; c++)
            {
                for (int y = 0; y < height; y++)
                {
                    int offset = y * width;
                    combinedRaw[ptr++] = channels[c][offset];
                    for (int x = 1; x < width; x++)
                    {
                        combinedRaw[ptr++] = unchecked((byte)(channels[c][offset + x] - channels[c][offset + x - 1]));
                    }
                }
            }
        }
        else
        {
            for (int c = 0; c < channels.Length; c++)
            {
                Buffer.BlockCopy(channels[c], 0, combinedRaw, ptr, channels[c].Length);
                ptr += channels[c].Length;
            }
        }

        using (var zlibStream = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlibStream.Write(combinedRaw);
        }

        return ms.ToArray();
    }
}
