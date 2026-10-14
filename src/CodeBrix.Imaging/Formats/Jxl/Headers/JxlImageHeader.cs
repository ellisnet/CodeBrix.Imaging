// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-image/src/lib.rs and color.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Headers;

internal sealed partial class JxlImageHeader
{
    internal JxlSizeHeader Size;
    internal uint Orientation = 1;
    internal JxlSizeHeader? IntrinsicSize;
    internal JxlSizeHeader? Preview;
    internal bool IsAnimated;
    internal uint TpsNumerator = 100;
    internal uint TpsDenominator = 1;
    internal uint LoopCount;
    internal bool HaveTimecodes;
    internal JxlBitDepth BitDepth = JxlBitDepth.Default;
    internal bool Modular16BitBuffers = true;
    internal JxlExtraChannel[] ExtraChannels = [];
    internal bool XybEncoded = true;
    internal JxlColorEncoding Color = new();
    internal float IntensityTarget = 255;
    internal float MinNits;
    internal bool RelativeToMaxDisplay;
    internal float LinearBelow;
    internal JxlExtensions Extensions = new();
    internal bool CustomOpsin;
    internal float[] Opsin =
    [
        11.031566901960783f, -9.866943921568629f, -0.16462299647058826f,
        -3.254147380392157f, 4.418770392156863f, -0.16462299647058826f,
        -3.6588512862745097f, 2.7129230470588235f, 1.9459282392156863f,
        -0.0037930732552754493f, -0.0037930732552754493f, -0.0037930732552754493f,
        1.0f - 0.05465007330715401f, 1.0f - 0.07005449891748593f, 1.0f - 0.049935103337343655f, 0.145f,
    ];
    internal uint CustomWeightsMask;
    internal float[] Up2 = DefaultUp2;
    internal float[] Up4 = DefaultUp4;
    internal float[] Up8 = DefaultUp8;

    internal int ColorChannels => this.Color.ColorSpace == 1 ? 1 : 3;
    internal uint DisplayWidth => this.Orientation > 4 ? this.Size.Height : this.Size.Width;
    internal uint DisplayHeight => this.Orientation > 4 ? this.Size.Width : this.Size.Height;

    internal JxlImageHeader ForPreview()
    {
        if (!this.Preview.HasValue) { throw new InvalidOperationException("The image has no preview."); }
        JxlImageHeader header = (JxlImageHeader)this.MemberwiseClone(); header.Size = this.Preview.Value; header.Preview = null; return header;
    }

    internal static JxlImageHeader Read(JxlBitReader reader, long maxPixels, long maxMetadataBytes)
    {
        if (reader.ReadBits(16) != 0x0aff) { JxlThrowHelper.Invalid("codestream signature mismatch."); }
        JxlImageHeader header = new() { Size = JxlSizeHeader.Read(reader) };
        ValidateSize(header.Size, maxPixels);
        bool allDefault = reader.ReadBool();
        bool extra = !allDefault && reader.ReadBool();
        if (extra)
        {
            header.Orientation = 1 + reader.ReadBits(3);
            if (reader.ReadBool()) { header.IntrinsicSize = JxlSizeHeader.Read(reader); }
            if (reader.ReadBool())
            {
                header.Preview = ReadPreview(reader);
                ValidateSize(header.Preview.Value, maxPixels);
            }

            header.IsAnimated = reader.ReadBool();
            if (header.IsAnimated)
            {
                header.TpsNumerator = reader.ReadU32(100, 1000, U(10, 1), U(30, 1));
                header.TpsDenominator = reader.ReadU32(1, 1001, U(8, 1), U(10, 1));
                header.LoopCount = reader.ReadU32(0, U(3), U(16), U(32));
                header.HaveTimecodes = reader.ReadBool();
            }
        }

        if (!allDefault)
        {
            header.BitDepth = JxlBitDepth.Read(reader);
            header.Modular16BitBuffers = reader.ReadBool();
            uint count = reader.ReadU32(0, 1, U(4, 2), U(12, 1));
            if (count > 256) { JxlThrowHelper.Invalid("extra channel count exceeds Level 10."); }
            header.ExtraChannels = new JxlExtraChannel[count];
            for (int i = 0; i < count; i++) { header.ExtraChannels[i] = JxlExtraChannel.Read(reader); }
            header.XybEncoded = reader.ReadBool();
            header.Color = JxlColorEncoding.Read(reader);
        }

        if (extra && !reader.ReadBool())
        {
            header.IntensityTarget = reader.ReadF16();
            header.MinNits = reader.ReadF16();
            header.RelativeToMaxDisplay = reader.ReadBool();
            header.LinearBelow = reader.ReadF16();
            if (header.IntensityTarget <= 0 || header.MinNits < 0 || header.MinNits > header.IntensityTarget ||
                header.LinearBelow < 0 || (header.RelativeToMaxDisplay && header.LinearBelow > 1))
            {
                JxlThrowHelper.Invalid("invalid tone mapping parameters.");
            }
        }

        if (!allDefault) { header.Extensions = JxlExtensions.Read(reader, maxMetadataBytes); }
        if (!reader.ReadBool())
        {
            if (header.XybEncoded && !reader.ReadBool())
            {
                header.CustomOpsin = true;
                ReadWeights(reader, header.Opsin);
            }

            header.CustomWeightsMask = reader.ReadBits(3);
            if ((header.CustomWeightsMask & 1) != 0) { header.Up2 = new float[15]; ReadWeights(reader, header.Up2); }
            if ((header.CustomWeightsMask & 2) != 0) { header.Up4 = new float[55]; ReadWeights(reader, header.Up4); }
            if ((header.CustomWeightsMask & 4) != 0) { header.Up8 = new float[210]; ReadWeights(reader, header.Up8); }
        }

        return header;
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteBits(16, 0x0aff);
        this.Size.Write(writer);
        writer.WriteBool(false); // Explicit metadata, with all values preserved.
        writer.WriteBool(true);
        writer.WriteBits(3, this.Orientation - 1);
        writer.WriteBool(this.IntrinsicSize.HasValue);
        this.IntrinsicSize?.Write(writer);
        writer.WriteBool(this.Preview.HasValue);
        if (this.Preview.HasValue) { WritePreview(writer, this.Preview.Value); }
        writer.WriteBool(this.IsAnimated);
        if (this.IsAnimated)
        {
            writer.WriteU32(this.TpsNumerator, 100, 1000, U(10, 1), U(30, 1));
            writer.WriteU32(this.TpsDenominator, 1, 1001, U(8, 1), U(10, 1));
            writer.WriteU32(this.LoopCount, 0, U(3), U(16), U(32));
            writer.WriteBool(this.HaveTimecodes);
        }

        this.BitDepth.Write(writer);
        writer.WriteBool(this.Modular16BitBuffers);
        writer.WriteU32((uint)this.ExtraChannels.Length, 0, 1, U(4, 2), U(12, 1));
        foreach (JxlExtraChannel channel in this.ExtraChannels) { channel.Write(writer); }
        writer.WriteBool(this.XybEncoded);
        this.Color.Write(writer);
        writer.WriteBool(false);
        writer.WriteF16(this.IntensityTarget);
        writer.WriteF16(this.MinNits);
        writer.WriteBool(this.RelativeToMaxDisplay);
        writer.WriteF16(this.LinearBelow);
        this.Extensions.Write(writer);
        bool defaultMatrix = !this.CustomOpsin && this.CustomWeightsMask == 0;
        writer.WriteBool(defaultMatrix);
        if (!defaultMatrix)
        {
            if (this.XybEncoded)
            {
                writer.WriteBool(!this.CustomOpsin);
                if (this.CustomOpsin) { WriteWeights(writer, this.Opsin); }
            }

            writer.WriteBits(3, this.CustomWeightsMask);
            if ((this.CustomWeightsMask & 1) != 0) { WriteWeights(writer, this.Up2); }
            if ((this.CustomWeightsMask & 2) != 0) { WriteWeights(writer, this.Up4); }
            if ((this.CustomWeightsMask & 4) != 0) { WriteWeights(writer, this.Up8); }
        }
    }

    internal static void ValidateSize(JxlSizeHeader size, long maxPixels)
    {
        if (size.Width == 0 || size.Height == 0 || size.Width > int.MaxValue || size.Height > int.MaxValue ||
            (ulong)size.Width * size.Height > (ulong)maxPixels)
        {
            JxlThrowHelper.Invalid("dimensions exceed MaxPixels or the supported buffer dimensions.");
        }
    }

    private static JxlSizeHeader ReadPreview(JxlBitReader reader)
    {
        bool div8 = reader.ReadBool();
        uint height = div8 ? 8 * reader.ReadU32(16, 32, U(5, 1), U(9, 33)) : reader.ReadU32(U(6, 1), U(8, 65), U(10, 321), U(12, 1345));
        uint ratio = reader.ReadBits(3);
        uint wDiv8 = div8 ? reader.ReadU32(16, 32, U(5, 1), U(9, 33)) : 1;
        uint width = !div8 ? reader.ReadU32(U(6, 1), U(8, 65), U(10, 321), U(12, 1345)) : ratio == 0 ? 8 * wDiv8 : JxlSizeHeader.RatioWidth(ratio, height);
        return new(width, height);
    }

    private static void WritePreview(JxlBitWriter writer, JxlSizeHeader preview)
    {
        writer.WriteBool(false);
        writer.WriteU32(preview.Height, U(6, 1), U(8, 65), U(10, 321), U(12, 1345));
        writer.WriteBits(3, 0);
        writer.WriteU32(preview.Width, U(6, 1), U(8, 65), U(10, 321), U(12, 1345));
    }

    private static void ReadWeights(JxlBitReader reader, float[] weights)
    {
        for (int i = 0; i < weights.Length; i++) { weights[i] = reader.ReadF16(); }
    }

    private static void WriteWeights(JxlBitWriter writer, float[] weights)
    {
        foreach (float weight in weights) { writer.WriteF16(weight); }
    }
}
