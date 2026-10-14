// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/filter.rs and header.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.Headers;

internal sealed class JxlRestorationFilter
{
    internal bool GaborEnabled = true;
    internal float[] GaborWeights = [0.115169525f, 0.061248592f, 0.115169525f, 0.061248592f, 0.115169525f, 0.061248592f];
    internal uint EpfIterations = 2;
    internal float[] Sharpness = [0, 1f / 7, 2f / 7, 3f / 7, 4f / 7, 5f / 7, 6f / 7, 1];
    internal float[] ChannelScale = [40, 5, 3.5f];
    internal float QuantMultiplier = 0.46f;
    internal float Pass0Scale = 0.9f;
    internal float Pass2Scale = 6.5f;
    internal float BorderMultiplier = 2f / 3;
    internal float ModularSigma = 1;
    internal JxlExtensions Extensions = new();

    internal static JxlRestorationFilter Read(JxlBitReader reader, bool modular, long maxMetadataBytes)
    {
        JxlRestorationFilter filter = new();
        if (reader.ReadBool()) { return filter; }
        filter.GaborEnabled = reader.ReadBool();
        if (filter.GaborEnabled && reader.ReadBool())
        {
            ReadWeights(reader, filter.GaborWeights);
            for (int i = 0; i < 6; i += 2)
            {
                if (Math.Abs(1 + ((filter.GaborWeights[i] + filter.GaborWeights[i + 1]) * 4)) < 1.1920929E-7f)
                { JxlThrowHelper.Invalid("Gaborish weights cannot be normalized."); }
            }
        }
        filter.EpfIterations = reader.ReadBits(2);
        if (filter.EpfIterations != 0)
        {
            if (!modular && reader.ReadBool()) { ReadWeights(reader, filter.Sharpness); }
            if (reader.ReadBool()) { ReadWeights(reader, filter.ChannelScale); reader.ReadBits(32); }
            if (reader.ReadBool())
            {
                if (!modular) { filter.QuantMultiplier = reader.ReadF16(); }
                filter.Pass0Scale = reader.ReadF16();
                filter.Pass2Scale = reader.ReadF16();
                filter.BorderMultiplier = reader.ReadF16();
            }
            if (modular) { filter.ModularSigma = reader.ReadF16(); }
        }
        filter.Extensions = JxlExtensions.Read(reader, maxMetadataBytes);
        return filter;
    }

    private static void ReadWeights(JxlBitReader reader, float[] values)
    {
        for (int i = 0; i < values.Length; i++) { values[i] = reader.ReadF16(); }
    }

    internal void Write(JxlBitWriter writer, bool modular)
    {
        JxlRestorationFilter defaults = new();
        writer.WriteBool(false); writer.WriteBool(this.GaborEnabled);
        if (this.GaborEnabled) { WriteWeights(writer, this.GaborWeights, defaults.GaborWeights); }
        writer.WriteBits(2, this.EpfIterations);
        if (this.EpfIterations != 0)
        {
            if (!modular) { WriteWeights(writer, this.Sharpness, defaults.Sharpness); }
            bool customChannels = !this.ChannelScale.AsSpan().SequenceEqual(defaults.ChannelScale);
            writer.WriteBool(customChannels);
            if (customChannels) { foreach (float value in this.ChannelScale) { writer.WriteF16(value); } writer.WriteBits(32, 0); }
            bool customScale = (!modular && this.QuantMultiplier != defaults.QuantMultiplier) || this.Pass0Scale != defaults.Pass0Scale ||
                this.Pass2Scale != defaults.Pass2Scale || this.BorderMultiplier != defaults.BorderMultiplier;
            writer.WriteBool(customScale);
            if (customScale)
            {
                if (!modular) { writer.WriteF16(this.QuantMultiplier); }
                writer.WriteF16(this.Pass0Scale); writer.WriteF16(this.Pass2Scale); writer.WriteF16(this.BorderMultiplier);
            }
            if (modular) { writer.WriteF16(this.ModularSigma); }
        }
        this.Extensions.Write(writer);
    }

    private static void WriteWeights(JxlBitWriter writer, float[] values, float[] defaults)
    {
        bool custom = !values.AsSpan().SequenceEqual(defaults);
        writer.WriteBool(custom);
        if (custom) { foreach (float value in values) { writer.WriteF16(value); } }
    }
}
