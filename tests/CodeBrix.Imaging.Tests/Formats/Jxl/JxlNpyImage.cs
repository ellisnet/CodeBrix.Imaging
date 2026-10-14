// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Independent reader for the NumPy float32 reference-array file format.

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

internal sealed class JxlNpyImage : IDisposable
{
    private readonly Stream stream;
    private readonly long dataOffset;
    internal int Frames { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int Channels { get; }

    internal static JxlNpyImage Open(string path)
    {
        FileStream file = File.OpenRead(path);
        try { return new(file); }
        catch { file.Dispose(); throw; }
    }

    internal JxlNpyImage(Stream stream)
    {
        this.stream = stream;
        Span<byte> prefix = stackalloc byte[8];
        stream.ReadExactly(prefix);
        if (!prefix[..6].SequenceEqual(new byte[] { 0x93, 78, 85, 77, 80, 89 })) { throw new InvalidDataException("Invalid NPY signature."); }
        if (prefix[6] is < 1 or > 3 || prefix[7] != 0) { throw new InvalidDataException("Unsupported NPY version."); }
        Span<byte> length = stackalloc byte[4];
        stream.ReadExactly(length[..(prefix[6] == 1 ? 2 : 4)]);
        uint size = prefix[6] == 1 ? BinaryPrimitives.ReadUInt16LittleEndian(length) : BinaryPrimitives.ReadUInt32LittleEndian(length);
        if (size > 65536) { throw new InvalidDataException("Oversized NPY header."); }
        byte[] bytes = new byte[size];
        stream.ReadExactly(bytes);
        string header = Encoding.UTF8.GetString(bytes);
        if (!Regex.IsMatch(header, "['\"]descr['\"]\\s*:\\s*['\"]<f4['\"]", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(header, "['\"]fortran_order['\"]\\s*:\\s*False", RegexOptions.CultureInvariant))
        { throw new InvalidDataException("Reference must be little-endian float32 in C order."); }
        Match shape = Regex.Match(header, "['\"]shape['\"]\\s*:\\s*\\(([0-9, ]+)\\)", RegexOptions.CultureInvariant);
        if (!shape.Success) { throw new InvalidDataException("Missing NPY shape."); }
        int[] dimensions = shape.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (dimensions.Length != 4 || dimensions.Any(value => value <= 0)) { throw new InvalidDataException("Expected frames, height, width, channels."); }
        this.Frames = dimensions[0];
        this.Height = dimensions[1];
        this.Width = dimensions[2];
        this.Channels = dimensions[3];
        this.dataOffset = stream.Position;
        long expected = checked((long)this.Frames * this.Width * this.Height * this.Channels * 4);
        if (expected != stream.Length - this.dataOffset) { throw new InvalidDataException("NPY payload does not match its shape."); }
    }

    internal float[] ReadFrame(int index)
    {
        if ((uint)index >= this.Frames) { throw new ArgumentOutOfRangeException(nameof(index)); }
        int samples = checked(this.Width * this.Height * this.Channels);
        this.stream.Position = this.dataOffset + ((long)index * samples * 4);
        float[] result = new float[samples];
        Span<byte> bytes = stackalloc byte[4096];
        for (int i = 0; i < samples;)
        {
            int count = Math.Min(bytes.Length / 4, samples - i);
            this.stream.ReadExactly(bytes[..(count * 4)]);
            for (int j = 0; j < count; j++) { result[i++] = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(j * 4, 4)); }
        }
        return result;
    }

    public void Dispose() => this.stream.Dispose();
}
