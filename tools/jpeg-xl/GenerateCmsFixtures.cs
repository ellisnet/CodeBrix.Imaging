// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Development-only generator. Build in a temporary friend-assembly console project.
// Historical skcms LUT output samples reproduce its matrix-folded XYZ scaling.
// JxlColorTests now validates lut8/lut16 against LookupTag's defining polynomial
// and ICC PCS encoding; those four native outputs are not correctness oracles.
// Its CMYK A2B sample comparison also corrects skcms's missing legacy Lab scale.
using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CodeBrix.Imaging;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;

if (Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS") != "1")
    throw new InvalidOperationException("Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 before running native references.");
if (args.Length != 2) throw new ArgumentException("Usage: generator repository-root skcms-reference-executable");
string repo = Path.GetFullPath(args[0]), executable = Path.GetFullPath(args[1]);
string resources = Path.Combine(repo, "tests/CodeBrix.Imaging.Tests/SampleFiles");
string output = Path.Combine(resources, "JxlSynthetic/Color");
long total = 0;
foreach (string directory in new[] { "JxlSynthetic", "JxlConformance" })
    foreach (string path in Directory.EnumerateFiles(Path.Combine(resources, directory), "*", SearchOption.AllDirectories)) total += new FileInfo(path).Length;
if (total + 200_000 > 100_000_000) throw new InvalidOperationException("JPEG XL test resource cap would be exceeded.");
Directory.CreateDirectory(output);
string temporary = Path.Combine(Path.GetTempPath(), "codebrix-skcms-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
Dictionary<string, string> hashes = new();
try
{
    foreach (string name in new[] { "grayscale", "cmyk_layers", "patches", "bench_oriented_brg", "pq", "hlg", "lut8", "lut16" })
    {
        JxlColorEncoding color;
        bool cmyk = name == "cmyk_layers";
        byte[] icc;
        if (name is "pq" or "hlg" or "lut8" or "lut16")
        {
            color = new() { Primaries = name.StartsWith("lut") ? 1U : 9U, TransferFunction = name == "pq" ? 16U : name == "hlg" ? 18U : 8U };
            icc = name.StartsWith("lut") ? LookupProfile(name == "lut8" ? 1 : 2) : JxlIccSynthesis.Create(color);
            File.WriteAllBytes(Path.Combine(output, name + ".icc"), icc);
        }
        else
        {
            using FileStream file = File.OpenRead(Path.Combine(resources, "JxlConformance", name, "input.jxl"));
            using JxlCodestreamStream stream = new(file, 64 << 20, true, default);
            JxlBitReader reader = new(stream, default);
            color = JxlImageHeader.Read(reader, 100_000_000, 64 << 20).Color;
            icc = JxlIccCodec.Decode(JxlIccCodec.ReadEncoded(reader, 64 << 20, Configuration.Default.MemoryAllocator), 64 << 20);
        }
        string profile = Path.Combine(temporary, name + ".icc");
        File.WriteAllBytes(profile, icc);
        hashes[name + ".original.icc"] = Convert.ToHexStringLower(SHA256.HashData(icc));
        string input = Path.Combine(output, name + ".input.f32");
        using (BinaryWriter writer = new(File.Create(input)))
        {
            Random random = new(4815);
            for (int pixel = 0; pixel < 257; pixel++)
            {
                float[] channels = new float[4];
                for (int c = 0; c < 4; c++) channels[c] = (float)random.NextDouble();
                if (color.ColorSpace == 1) channels[1] = channels[2] = channels[0];
                if (!cmyk) channels[3] = 1;
                foreach (float channel in channels) writer.Write(channel);
            }
        }
        foreach (bool reverse in name is "cmyk_layers" or "pq" or "hlg" or "lut8" or "lut16" ? new[] { false, true } : new[] { false })
        {
            string target = Path.Combine(output, name + (reverse ? ".reverse.f32" : ".forward.f32"));
            ProcessStartInfo start = new(executable) { UseShellExecute = false };
            foreach (string argument in new[] { reverse ? "srgb" : profile, reverse ? profile : "srgb", input, target }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start);
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("Native CMS failed for " + name);
            hashes[Path.GetFileName(target)] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target)));
        }
        hashes[Path.GetFileName(input)] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input)));
    }
    File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
        copyright = "Copyright (c) 2026 Jeremy Ellis and contributors", license = "Apache-2.0",
        skcms_revision = "7e572ada698a60ff50bd9038fd6e01e44f40868a", architecture = "Linux x64 baseline",
        generator = "tools/jpeg-xl/GenerateCmsFixtures.cs", profile_source = "../JxlConformance: original ICC in each input.jxl; see its attribution",
        samples = 257, format = "little-endian RGBA float32; native CMYK uses inverted samples", sha256 = hashes
    }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
finally { Directory.Delete(temporary, true); }

// Original synthetic ICC v4 profiles exercise every mAB/mBA stage and a
// non-cubic CLUT. Profile/curve layout follows the managed ICC writer above.
static byte[] LookupProfile(int precision)
{
    byte[] source = JxlIccSynthesis.Create(new() { TransferFunction = 8 });
    List<(uint Signature, byte[] Data)> tags = new();
    int count = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(128));
    for (int i = 0; i < count; i++)
    {
        int entry = 132 + (12 * i), offset = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(entry + 4));
        int size = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(entry + 8));
        tags.Add((BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(entry)), source.AsSpan(offset, size).ToArray()));
    }
    tags.Add((0x41324231, LookupTag(false, precision))); tags.Add((0x42324131, LookupTag(true, precision)));
    int length = 132 + (12 * tags.Count);
    foreach (var tag in tags) length += (tag.Data.Length + 3) & ~3;
    // skcms vector gathers require up to two readable bytes past the last CLUT.
    length += 4;
    byte[] profile = new byte[length]; source.AsSpan(0, 128).CopyTo(profile);
    Put32(profile, 0, (uint)length); Put32(profile, 128, (uint)tags.Count);
    int at = 132 + (12 * tags.Count);
    for (int i = 0; i < tags.Count; i++)
    {
        Put32(profile, 132 + (12 * i), tags[i].Signature); Put32(profile, 136 + (12 * i), (uint)at);
        Put32(profile, 140 + (12 * i), (uint)tags[i].Data.Length);
        tags[i].Data.CopyTo(profile, at); at += (tags[i].Data.Length + 3) & ~3;
    }
    return profile;
}
static byte[] LookupTag(bool reverse, int precision)
{
    const int bOffset = 32, matrixOffset = 80, mOffset = 128, aOffset = 176, gridOffset = 224;
    byte[] tag = new byte[gridOffset + 20 + (2 * 3 * 4 * 3 * precision)];
    Put32(tag, 0, reverse ? 0x6d424120U : 0x6d414220U); tag[8] = tag[9] = 3;
    Put32(tag, 12, bOffset); Put32(tag, 16, matrixOffset); Put32(tag, 20, mOffset); Put32(tag, 24, gridOffset); Put32(tag, 28, aOffset);
    for (int c = 0; c < 3; c++)
    {
        foreach (var curve in new[] { (bOffset, 1.25f), (mOffset, .75f), (aOffset, 1.5f) })
        {
            int at = curve.Item1 + (16 * c); Put32(tag, at, 0x70617261);
            Put32(tag, at + 12, (uint)MathF.Round(curve.Item2 * 65536));
        }
    }
    float[] matrix = [.3f, .04f, .01f, .02f, .34f, .02f, .01f, .04f, .3f, .01f, .02f, .03f];
    for (int i = 0; i < 12; i++) Put32(tag, matrixOffset + (4 * i), (uint)MathF.Round(matrix[i] * 65536));
    tag[gridOffset] = 2; tag[gridOffset + 1] = 3; tag[gridOffset + 2] = 4; tag[gridOffset + 16] = (byte)precision;
    int position = gridOffset + 20;
    for (int r = 0; r < 2; r++) for (int g = 0; g < 3; g++) for (int b = 0; b < 4; b++)
    {
        float red = r, green = g / 2f, blue = b / 3f;
        float[] values = [(.6f * red) + (.2f * green) + (.1f * blue * green), (.1f * red) + (.7f * blue), (.4f * green) + (.3f * blue) + (.15f * red * blue)];
        foreach (float value in values)
        {
            if (precision == 1) tag[position++] = (byte)MathF.Round(value * 255);
            else { BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(position), (ushort)MathF.Round(value * 65535)); position += 2; }
        }
    }
    return tag;
}
static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
