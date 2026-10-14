// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Linq;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using Xunit;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlFrameHeaderTests
{
    [Theory]
    [InlineData(2, "2", "2", false)]
    [InlineData(2, "2", "7", false)]
    [InlineData(2, "4,2,1", "0,1,2", false)]
    [InlineData(3, "4,4", "0,1", false)]
    [InlineData(3, "2,4", "0,1", false)]
    [InlineData(3, "4,2", "1,1", false)]
    [InlineData(3, "4,2", "2,1", false)]
    [InlineData(2, "4,2", "0,1", true)]
    [InlineData(4, "8,4,2,1", "0,1,2,3", true)]
    [InlineData(2, "", "", true)]
    [InlineData(11, "8,4,2,1", "0,2,4,7", true)]
    public void progressive_header_rejects_invalid_sequences_and_accepts_equal_pass_and_downsample_counts(
        uint passes, string downsample, string lastPass, bool valid)
    {
        uint[] downs = Parse(downsample), lasts = Parse(lastPass);
        JxlImageHeader image = new() { Size = new(1, 1), XybEncoded = false };
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        // Write the fields explicitly so malformed test input does not depend
        // on the production writer accepting an invalid frame.
        writer.WriteBool(false); writer.WriteBits(2, 0); writer.WriteBool(true);
        writer.WriteU64(0); writer.WriteBool(false); writer.WriteU32(1, 1, 2, 4, 8);
        writer.WriteBits(2, 1); writer.WriteU32(passes, 1, 2, 3, U(3, 4));
        writer.WriteU32((uint)downs.Length, 0, 1, 2, U(1, 3));
        for (int i = 0; i < passes - 1; i++) { writer.WriteBits(2, 0); }
        foreach (uint value in downs) { writer.WriteU32(value, 1, 2, 4, 8); }
        foreach (uint value in lasts) { writer.WriteU32(value, 0, 1, 2, U(3)); }
        writer.WriteBool(false); writer.WriteU32(0, 0, 1, 2, U(2, 3)); writer.WriteBool(true);
        JxlName.Write(writer, string.Empty); new JxlRestorationFilter().Write(writer, true);
        new JxlExtensions().Write(writer); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        if (valid)
        {
            JxlFrameHeader frame = JxlFrameHeader.Read(reader, image, 100, 1024);
            Assert.Equal(passes, frame.PassCount); Assert.Equal(downs, frame.Downsample); Assert.Equal(lasts, frame.LastPass);
        }
        else
        {
            Assert.Contains("progressive", Assert.Throws<InvalidImageContentException>(() => JxlFrameHeader.Read(reader, image, 100, 1024)).Message);
            using MemoryStream output = new();
            JxlFrameHeader invalid = new() { Width = 1, Height = 1, Modular = true, PassCount = passes,
                PassShifts = new uint[passes - 1], Downsample = downs, LastPass = lasts };
            Assert.Throws<InvalidImageContentException>(() => invalid.Write(new(output), image));
            Assert.Equal(0, output.Length);
        }
    }

    private static uint[] Parse(string value) => value.Length == 0 ? [] : value.Split(',').Select(uint.Parse).ToArray();
}
