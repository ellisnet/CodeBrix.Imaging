// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Color;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging.Formats.Jxl;

internal sealed partial class JxlDecoderCore
{
    internal JxlRawImage DecodeRaw(Configuration configuration, Stream stream, CancellationToken cancellationToken)
    {
        List<JxlRawFrame> frames = [];
        bool coalesce = this.options.Coalesce;
        try
        {
            (ImageMetadata metadata, JxlImageHeader header) = this.DecodeFrames(configuration, stream, coalesce, true,
                (image, frame, rendered, modular, color) => frames.Add(this.CreateRawFrame(image, frame, rendered, modular, color, coalesce, cancellationToken)), cancellationToken);
            bool orient = coalesce && this.options.ApplyOrientation;
            JxlRawImage result = new(frames.ToArray(), metadata, (int)(orient ? header.DisplayWidth : header.Size.Width),
                (int)(orient ? header.DisplayHeight : header.Size.Height), coalesce);
            frames.Clear();
            return result;
        }
        finally { foreach (JxlRawFrame frame in frames) { frame.Dispose(); } }
    }

    private JxlRawFrame CreateRawFrame(JxlImageHeader header, JxlFrameHeader frame, JxlFloatFrame rendered,
        JxlModularFrame modular, JxlOutputColorTransform outputColor, bool coalesce, CancellationToken cancellationToken)
    {
        int colors = outputColor.PreserveCoded ? rendered.ColorChannels : outputColor.OutputSrgb ? 3 : header.Color.ColorSpace == 1 ? 1 : rendered.ColorChannels;
        JxlRawPlane[] planes = new JxlRawPlane[colors + header.ExtraChannels.Length];
        bool convert = !outputColor.IsIdentity(rendered);
        JxlColorTransform transform = rendered.ColorTransformed || outputColor.PreserveCoded ? null : new(header, frame.YCbCr);
        int orientation = coalesce && this.options.ApplyOrientation ? (int)header.Orientation : 1;
        bool fullLayer = frame.Left == 0 && frame.Top == 0 && frame.Width == header.Size.Width && frame.Height == header.Size.Height;
        bool exactColor = !header.XybEncoded && !frame.YCbCr && !frame.Restoration.GaborEnabled && frame.Restoration.EpfIterations == 0;
        try
        {
            for (int c = 0; c < planes.Length; c++)
            {
                int source = c < colors ? Math.Min(c, rendered.ColorChannels - 1) : rendered.ColorChannels + c - colors;
                JxlExtraChannel extra = c < colors ? null : header.ExtraChannels[c - colors];
                JxlBitDepth depth = extra?.BitDepth ?? header.BitDepth;
                JxlModularPlane original = modular?.Planes[source];
                uint mode = extra == null ? frame.Blend.Mode : frame.ExtraBlending[c - colors].Mode;
                bool exactInteger = original != null && depth.ExponentBits == 0 && (frame.Flags & (extra == null ? 0x13UL : 2UL)) == 0 &&
                    (extra != null || (exactColor && !convert)) && (!coalesce || (fullLayer && mode == 0)) &&
                    original.Width == rendered.Planes[source].Width && original.Height == rendered.Planes[source].Height;
                int width = rendered.Planes[source].Width, height = rendered.Planes[source].Height;
                int outWidth = orientation > 4 ? height : width, outHeight = orientation > 4 ? width : height;
                Buffer2D<int> integers = null;
                Buffer2D<float> floats = null;
                try
                {
                    if (exactInteger) { integers = rendered.Allocator.Allocate2D<int>(outWidth, outHeight); }
                    else { floats = rendered.Allocator.Allocate2D<float>(outWidth, outHeight); }
                    for (int y = 0; y < height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = 0; x < width; x++)
                        {
                            (int dx, int dy) = Orient(x, y, width, height, orientation);
                            if (exactInteger) { integers.DangerousGetRowSpan(dy)[dx] = original.Row(y)[x]; }
                            else if (c < colors && convert)
                            {
                                Vector3 input = new(rendered.Planes[0].DangerousGetRowSpan(y)[x],
                                    rendered.Planes[rendered.ColorChannels == 1 ? 0 : 1].DangerousGetRowSpan(y)[x],
                                    rendered.Planes[rendered.ColorChannels == 1 ? 0 : 2].DangerousGetRowSpan(y)[x]);
                                float black = outputColor.BlackIndex < 0 ? 0 : rendered.Planes[rendered.ColorChannels + outputColor.BlackIndex].DangerousGetRowSpan(y)[x];
                                Vector3 output = outputColor.Convert(input, black, rendered, transform);
                                floats.DangerousGetRowSpan(dy)[dx] = c == 0 ? output.X : c == 1 ? output.Y : output.Z;
                            }
                            else { floats.DangerousGetRowSpan(dy)[dx] = rendered.Planes[source].DangerousGetRowSpan(y)[x]; }
                        }
                    }
                    planes[c] = new(integers, floats, exactInteger ? depth : new JxlBitDepth(32, 8), c < colors ? c : -1, extra);
                    integers = null; floats = null;
                }
                finally { integers?.Dispose(); floats?.Dispose(); }
            }
            JxlFrameMetadata metadata = new();
            CopyFrameMetadata(frame, metadata);
            JxlRawColorSpace colorSpace = outputColor.PreserveCoded && !rendered.ColorTransformed && header.XybEncoded ? JxlRawColorSpace.Xyb
                : outputColor.PreserveCoded && !rendered.ColorTransformed && frame.YCbCr ? JxlRawColorSpace.YCbCr
                : !outputColor.OutputSrgb && Array.Exists(header.ExtraChannels, c => c.Type == 4) ? JxlRawColorSpace.Cmyk
                : colors == 1 ? JxlRawColorSpace.Grayscale : JxlRawColorSpace.Rgb;
            return new(planes, metadata, colors, planes[0].Width, planes[0].Height, colorSpace);
        }
        catch { foreach (JxlRawPlane plane in planes) { plane?.Dispose(); } throw; }
    }

    private static (int X, int Y) Orient(int x, int y, int width, int height, int orientation) => orientation switch
    {
        2 => (width - 1 - x, y), 3 => (width - 1 - x, height - 1 - y), 4 => (x, height - 1 - y),
        5 => (y, x), 6 => (height - 1 - y, x), 7 => (height - 1 - y, width - 1 - x), 8 => (y, width - 1 - x), _ => (x, y),
    };

    private static void CopyFrameMetadata(JxlFrameHeader source, JxlFrameMetadata target)
    {
        target.LowFrequencyLevel = (int)source.LfLevel;
        target.Duration = source.Duration; target.Timecode = source.Timecode; target.Name = source.Name;
        target.IsKeyframe = source.IsKeyframe; target.IsReferenceOnly = source.FrameType == 2;
        target.CropLeft = source.Left; target.CropTop = source.Top; target.CropWidth = (int)source.Width; target.CropHeight = (int)source.Height;
        target.Blending = CopyBlending(source.Blend);
        target.ExtraChannelBlending = Array.ConvertAll(source.ExtraBlending, CopyBlending);
        target.SaveAsReference = source.CanReference ? (int)source.SaveAsReference : -1;
        target.SaveBeforeColorTransform = source.SaveBeforeColorTransform;
    }

    private static JxlBlendInfo CopyBlending(JxlBlendingInfo info) => new((JxlBlendMode)info.Mode, (int)info.AlphaChannel, info.Clamp, (int)info.Source);
}
