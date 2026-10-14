// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/header.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Per-extra-channel blending-source presence and progressive pass validation
// follow libjxl frame_header.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using System.Numerics;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Headers;

internal sealed class JxlFrameHeader
{
    internal uint FrameType;
    internal bool Modular;
    internal ulong Flags;
    internal bool YCbCr;
    internal uint[] JpegUpsampling = new uint[3];
    internal uint Upsampling = 1;
    internal uint[] ExtraUpsampling = [];
    internal uint GroupSizeShift = 1;
    internal uint XQuantScale = 3, BQuantScale = 2;
    internal uint PassCount = 1;
    internal uint[] PassShifts = [];
    internal uint[] Downsample = [];
    internal uint[] LastPass = [];
    internal uint LfLevel;
    internal bool HaveCrop;
    internal int Left, Top;
    internal uint Width, Height;
    internal JxlBlendingInfo Blend;
    internal JxlBlendingInfo[] ExtraBlending = [];
    internal uint Duration, Timecode;
    internal bool IsLast = true;
    internal uint SaveAsReference;
    internal bool SaveBeforeColorTransform;
    internal string Name = string.Empty;
    internal JxlRestorationFilter Restoration = new();
    internal JxlExtensions Extensions = new();

    internal bool IsNormal => this.FrameType is 0 or 3;
    internal bool IsKeyframe => this.IsNormal && (this.IsLast || this.Duration != 0);
    internal bool CanReference => !this.IsLast && (this.Duration == 0 || this.SaveAsReference != 0) && this.FrameType != 1;
    internal bool UseLfFrame => (this.Flags & 32) != 0;
    internal JxlFrameHeader Clone() => (JxlFrameHeader)this.MemberwiseClone();
    internal uint SampleWidth => DivideRoundUp(DivideRoundUp(this.Width, this.Upsampling), 1U << (3 * (int)this.LfLevel));
    internal uint SampleHeight => DivideRoundUp(DivideRoundUp(this.Height, this.Upsampling), 1U << (3 * (int)this.LfLevel));
    internal uint GroupDimension => 128U << (int)this.GroupSizeShift;
    internal ulong GroupCount => (ulong)DivideRoundUp(this.SampleWidth, this.GroupDimension) * DivideRoundUp(this.SampleHeight, this.GroupDimension);
    internal ulong LfGroupCount => (ulong)DivideRoundUp(this.SampleWidth, this.GroupDimension * 8) * DivideRoundUp(this.SampleHeight, this.GroupDimension * 8);

    internal static uint DivideRoundUp(uint size, uint divisor) => (size / divisor) + (size % divisor == 0 ? 0U : 1U);

    internal bool ResetsCanvas(JxlImageHeader image, uint mode) => mode == 0 && (!this.HaveCrop ||
        (this.Left <= 0 && this.Top <= 0 && this.Left + (long)this.Width >= image.Size.Width && this.Top + (long)this.Height >= image.Size.Height));

    internal static JxlFrameHeader Read(JxlBitReader reader, JxlImageHeader image, long maxPixels, long maxMetadataBytes)
    {
        reader.ZeroPadToByte();
        JxlFrameHeader frame = new()
        {
            Width = image.Size.Width, Height = image.Size.Height, XQuantScale = image.XybEncoded ? 3U : 2U,
            ExtraUpsampling = new uint[image.ExtraChannels.Length], ExtraBlending = new JxlBlendingInfo[image.ExtraChannels.Length],
        };
        System.Array.Fill(frame.ExtraUpsampling, 1U);
        if (reader.ReadBool()) { frame.Validate(image); return frame; }
        frame.FrameType = reader.ReadBits(2);
        frame.Modular = reader.ReadBool();
        frame.Flags = reader.ReadU64();
        frame.YCbCr = !image.XybEncoded && reader.ReadBool();
        if (frame.YCbCr && !frame.UseLfFrame)
        {
            for (int i = 0; i < 3; i++) { frame.JpegUpsampling[i] = reader.ReadBits(2); }
        }
        if (!frame.UseLfFrame)
        {
            frame.Upsampling = reader.ReadU32(1, 2, 4, 8);
            for (int i = 0; i < frame.ExtraUpsampling.Length; i++) { frame.ExtraUpsampling[i] = reader.ReadU32(1, 2, 4, 8); }
        }
        if (frame.Modular) { frame.GroupSizeShift = reader.ReadBits(2); }
        frame.XQuantScale = image.XybEncoded && !frame.Modular ? reader.ReadBits(3) : 2;
        if (image.XybEncoded && !frame.Modular) { frame.BQuantScale = reader.ReadBits(3); }
        if (frame.FrameType != 2)
        {
            frame.PassCount = reader.ReadU32(1, 2, 3, U(3, 4));
            if (frame.PassCount != 1)
            {
                uint count = reader.ReadU32(0, 1, 2, U(1, 3));
                frame.PassShifts = new uint[frame.PassCount - 1];
                frame.Downsample = new uint[count];
                frame.LastPass = new uint[count];
                for (int i = 0; i < frame.PassShifts.Length; i++) { frame.PassShifts[i] = reader.ReadBits(2); }
                for (int i = 0; i < count; i++) { frame.Downsample[i] = reader.ReadU32(1, 2, 4, 8); }
                for (int i = 0; i < count; i++) { frame.LastPass[i] = reader.ReadU32(0, 1, 2, U(3)); }
            }
        }
        if (frame.FrameType == 1) { frame.LfLevel = 1 + reader.ReadBits(2); }
        frame.HaveCrop = frame.FrameType != 1 && reader.ReadBool();
        if (frame.HaveCrop)
        {
            if (frame.FrameType != 2)
            {
                frame.Left = JxlBitReader.UnpackSigned(ReadDimension(reader));
                frame.Top = JxlBitReader.UnpackSigned(ReadDimension(reader));
            }
            frame.Width = ReadDimension(reader);
            frame.Height = ReadDimension(reader);
        }
        if (frame.Width == 0 || frame.Height == 0 || (ulong)frame.Width * frame.Height > (ulong)maxPixels)
        { JxlThrowHelper.Invalid("frame dimensions exceed the configured pixel limit."); }

        if (frame.IsNormal)
        {
            frame.Blend = JxlBlendingInfo.Read(reader, image, frame);
            for (int i = 0; i < frame.ExtraBlending.Length; i++) { frame.ExtraBlending[i] = JxlBlendingInfo.Read(reader, image, frame); }
            if (image.IsAnimated)
            {
                frame.Duration = reader.ReadU32(0, 1, U(8), U(32));
                if (image.HaveTimecodes) { frame.Timecode = reader.ReadBits(32); }
            }
            frame.IsLast = reader.ReadBool();
        }
        else { frame.IsLast = false; }
        if (frame.FrameType != 1 && !frame.IsLast) { frame.SaveAsReference = reader.ReadBits(2); }
        frame.SaveBeforeColorTransform = !frame.IsNormal;
        if (frame.FrameType == 2 || (frame.ResetsCanvas(image, frame.Blend.Mode) && !frame.IsLast &&
            (frame.Duration == 0 || frame.SaveAsReference != 0) && frame.FrameType != 1))
        { frame.SaveBeforeColorTransform = reader.ReadBool(); }
        frame.Name = JxlName.Read(reader);
        frame.Restoration = JxlRestorationFilter.Read(reader, frame.Modular, maxMetadataBytes);
        frame.Extensions = JxlExtensions.Read(reader, maxMetadataBytes);
        frame.Validate(image);
        return frame;
    }

    private void Validate(JxlImageHeader image)
    {
        if (this.Downsample.Length != this.LastPass.Length || this.Downsample.Length > 4 || this.Downsample.Length > this.PassCount)
        { JxlThrowHelper.Invalid("invalid progressive downsample count."); }
        for (int i = 0; i < this.Downsample.Length; i++)
        {
            if (this.LastPass[i] >= this.PassCount ||
                (i != 0 && (this.Downsample[i] >= this.Downsample[i - 1] || this.LastPass[i] <= this.LastPass[i - 1])))
            { JxlThrowHelper.Invalid("invalid progressive downsample or last-pass sequence."); }
        }
        if (this.Width > 1U << 30 || this.Height > 1U << 30 || (ulong)this.Width * this.Height > 1UL << 40)
        { JxlThrowHelper.Invalid("frame dimensions exceed Level 10."); }
        if (this.UseLfFrame && this.LfLevel >= 4) { JxlThrowHelper.Invalid("invalid low-frequency reference level."); }
        ValidateBlending(this.Blend, image);
        foreach (JxlBlendingInfo info in this.ExtraBlending) { ValidateBlending(info, image); }
        int colorShift = BitOperations.Log2(this.Upsampling);
        for (int i = 0; i < this.ExtraUpsampling.Length; i++)
        {
            int shift = BitOperations.Log2(this.ExtraUpsampling[i]) + (int)image.ExtraChannels[i].DimShift;
            if (shift < colorShift || shift > 6 || shift - colorShift > 7 + this.GroupSizeShift)
            { JxlThrowHelper.Invalid("invalid cumulative extra-channel sampling shift."); }
        }
    }

    internal void Write(JxlBitWriter writer, JxlImageHeader image)
    {
        this.Validate(image);
        if (this.ExtraUpsampling.Length != image.ExtraChannels.Length || this.ExtraBlending.Length != image.ExtraChannels.Length ||
            this.PassCount is < 1 or > 11 || this.FrameType > 3 || this.GroupSizeShift > 3)
        { throw new ArgumentException("Invalid JPEG XL frame fields."); }
        writer.ZeroPadToByte(); writer.WriteBool(false);
        writer.WriteBits(2, this.FrameType); writer.WriteBool(this.Modular); writer.WriteU64(this.Flags);
        if (!image.XybEncoded) { writer.WriteBool(this.YCbCr); }
        if (this.YCbCr && !this.UseLfFrame)
        { foreach (uint value in this.JpegUpsampling) { writer.WriteBits(2, value); } }
        if (!this.UseLfFrame)
        {
            writer.WriteU32(this.Upsampling, 1, 2, 4, 8);
            foreach (uint value in this.ExtraUpsampling) { writer.WriteU32(value, 1, 2, 4, 8); }
        }
        if (this.Modular) { writer.WriteBits(2, this.GroupSizeShift); }
        if (image.XybEncoded && !this.Modular) { writer.WriteBits(3, this.XQuantScale); writer.WriteBits(3, this.BQuantScale); }
        if (this.FrameType != 2)
        {
            writer.WriteU32(this.PassCount, 1, 2, 3, U(3, 4));
            if (this.PassCount != 1)
            {
                if (this.PassShifts.Length != this.PassCount - 1 || this.Downsample.Length != this.LastPass.Length)
                { throw new ArgumentException("Invalid JPEG XL progressive pass fields."); }
                writer.WriteU32((uint)this.Downsample.Length, 0, 1, 2, U(1, 3));
                foreach (uint value in this.PassShifts) { writer.WriteBits(2, value); }
                foreach (uint value in this.Downsample) { writer.WriteU32(value, 1, 2, 4, 8); }
                foreach (uint value in this.LastPass) { writer.WriteU32(value, 0, 1, 2, U(3)); }
            }
        }
        if (this.FrameType == 1) { writer.WriteBits(2, this.LfLevel - 1); }
        else { writer.WriteBool(this.HaveCrop); }
        if (this.HaveCrop)
        {
            if (this.FrameType != 2) { WriteDimension(writer, JxlBitWriter.PackSigned(this.Left)); WriteDimension(writer, JxlBitWriter.PackSigned(this.Top)); }
            WriteDimension(writer, this.Width); WriteDimension(writer, this.Height);
        }
        if (this.IsNormal)
        {
            this.Blend.Write(writer, image, this);
            foreach (JxlBlendingInfo blend in this.ExtraBlending) { blend.Write(writer, image, this); }
            if (image.IsAnimated)
            { writer.WriteU32(this.Duration, 0, 1, U(8), U(32)); if (image.HaveTimecodes) { writer.WriteBits(32, this.Timecode); } }
            writer.WriteBool(this.IsLast);
        }
        if (this.FrameType != 1 && !this.IsLast) { writer.WriteBits(2, this.SaveAsReference); }
        if (this.FrameType == 2 || (this.ResetsCanvas(image, this.Blend.Mode) && !this.IsLast &&
            (this.Duration == 0 || this.SaveAsReference != 0) && this.FrameType != 1))
        { writer.WriteBool(this.SaveBeforeColorTransform); }
        JxlName.Write(writer, this.Name); this.Restoration.Write(writer, this.Modular); this.Extensions.Write(writer);
    }

    private static void ValidateBlending(JxlBlendingInfo info, JxlImageHeader image)
    {
        if (info.Mode is 2 or 3 && image.ExtraChannels.Length != 0 && image.ExtraChannels[info.AlphaChannel].Type != 0)
        { JxlThrowHelper.Invalid("frame blending requires an alpha extra channel."); }
    }

    private static uint ReadDimension(JxlBitReader reader) => reader.ReadU32(U(8), U(11, 256), U(14, 2304), U(30, 18688));
    private static void WriteDimension(JxlBitWriter writer, uint value) => writer.WriteU32(value, U(8), U(11, 256), U(14, 2304), U(30, 18688));
}

internal readonly record struct JxlBlendingInfo(uint Mode, uint AlphaChannel, bool Clamp, uint Source)
{
    internal void Write(JxlBitWriter writer, JxlImageHeader image, JxlFrameHeader frame)
    {
        writer.WriteU32(this.Mode, 0, 1, 2, U(2, 3));
        bool haveAlpha = image.ExtraChannels.Length != 0 && this.Mode is 2 or 3;
        if (haveAlpha) { writer.WriteU32(this.AlphaChannel, 0, 1, 2, U(3, 3)); }
        if (haveAlpha || this.Mode == 4) { writer.WriteBool(this.Clamp); }
        if (!frame.ResetsCanvas(image, this.Mode)) { writer.WriteBits(2, this.Source); }
    }

    internal static JxlBlendingInfo Read(JxlBitReader reader, JxlImageHeader image, JxlFrameHeader frame)
    {
        uint mode = reader.ReadU32(0, 1, 2, U(2, 3));
        if (mode > 4) { JxlThrowHelper.Invalid("invalid frame blending mode."); }
        bool haveAlpha = image.ExtraChannels.Length != 0 && mode is 2 or 3;
        uint alpha = haveAlpha ? reader.ReadU32(0, 1, 2, U(3, 3)) : 0;
        if (haveAlpha && alpha >= image.ExtraChannels.Length) { JxlThrowHelper.Invalid("invalid blending alpha channel."); }
        bool clamp = (haveAlpha || mode == 4) && reader.ReadBool();
        uint source = frame.ResetsCanvas(image, mode) ? 0 : reader.ReadBits(2);
        return new(mode, alpha, clamp, source);
    }
}
