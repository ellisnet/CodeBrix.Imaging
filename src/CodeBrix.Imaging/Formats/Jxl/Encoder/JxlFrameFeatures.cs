// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original integration of the separately attributed feature writers.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder;

internal sealed class JxlFrameFeatures
{
    private byte[] data;
    private long bits;
    internal ulong Flags { get; private set; }
    internal bool HasNoise => (this.Flags & 1) != 0;

    internal static JxlFrameFeatures Create(JxlRawFrameEncoding source, JxlImageHeader image, JxlFrameHeader frame, int effort, CancellationToken cancellationToken)
    {
        if (source.Patches == null || source.Splines == null) { throw new ArgumentException("Feature arrays cannot be null.", nameof(source)); }
        JxlFrameFeatures features = new(); using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        if (source.Patches.Length != 0)
        {
            JxlPatches.Patch[] patches = new JxlPatches.Patch[source.Patches.Length];
            for (int i = 0; i < patches.Length; i++)
            {
                JxlPatch input = source.Patches[i];
                if (input == null || input.Reference is < 0 or > 3 || input.Source.X < 0 || input.Source.Y < 0 || input.Source.Width <= 0 || input.Source.Height <= 0 || input.Targets == null)
                { throw new ArgumentException("Invalid JPEG XL patch source.", nameof(source)); }
                JxlPatches.Target[] targets = new JxlPatches.Target[input.Targets.Length];
                for (int t = 0; t < targets.Length; t++)
                {
                    JxlPatchTarget target = input.Targets[t];
                    if (target?.ExtraChannelBlending == null || (target.ExtraChannelBlending.Length != 0 && target.ExtraChannelBlending.Length != image.ExtraChannels.Length))
                    { throw new ArgumentException("Invalid JPEG XL patch extra-channel blending.", nameof(source)); }
                    JxlBlendingInfo[] blending = new JxlBlendingInfo[image.ExtraChannels.Length + 1];
                    blending[0] = Blend(target.Blending);
                    for (int c = 0; c < target.ExtraChannelBlending.Length; c++) { blending[c + 1] = Blend(target.ExtraChannelBlending[c]); }
                    targets[t] = new(target.Position.X, target.Position.Y, blending);
                }
                patches[i] = new((uint)input.Reference, (uint)input.Source.X, (uint)input.Source.Y, (uint)input.Source.Width, (uint)input.Source.Height, targets);
            }
            new JxlPatches(patches).Write(writer, image, frame, effort, cancellationToken); features.Flags |= 2;
        }
        if (source.Splines.Length != 0)
        {
            if (!image.XybEncoded) { throw new ArgumentException("Splines require XYB color coordinates.", nameof(source)); }
            JxlSplines.Create(source.Splines, source.SplineQuantizationAdjustment, frame).Write(writer, effort, cancellationToken); features.Flags |= 16;
        }
        if (source.NoiseLut != null)
        {
            if (!image.XybEncoded || source.NoiseLut.Length != 8) { throw new ArgumentException("Noise requires XYB coordinates and eight strengths.", nameof(source)); }
            foreach (float value in source.NoiseLut)
            {
                if (!float.IsFinite(value) || value < 0 || value > 1023f / 1024) { throw new ArgumentOutOfRangeException(nameof(source.NoiseLut)); }
                writer.WriteBits(10, (uint)MathF.Round(value * 1024));
            }
            features.Flags |= 1;
        }
        features.bits = writer.Position; writer.ZeroPadToByte(); features.data = stream.ToArray(); return features;

        JxlBlendingInfo Blend(JxlPatchBlendInfo info)
        {
            if (!Enum.IsDefined(info.Mode) || info.AlphaChannel < 0 || ((int)info.Mode >= 4 && info.AlphaChannel >= image.ExtraChannels.Length))
            { throw new ArgumentException("Invalid JPEG XL patch blending.", nameof(source)); }
            return new((uint)info.Mode, (uint)info.AlphaChannel, info.Clamp, 0);
        }
    }

    internal void Write(JxlBitWriter writer) => writer.WriteBitstream(this.data, this.bits);

    internal static JxlFrameFeatures WithNoise(JxlFrameFeatures features, float[] noise)
    {
        if (noise == null || features?.HasNoise == true) { return features; }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); features?.Write(writer);
        foreach (float value in noise) { writer.WriteBits(10, (uint)MathF.Round(value * 1024)); }
        long bits = writer.Position; writer.ZeroPadToByte();
        return new() { data = stream.ToArray(), bits = bits, Flags = (features?.Flags ?? 0) | 1 };
    }
}
