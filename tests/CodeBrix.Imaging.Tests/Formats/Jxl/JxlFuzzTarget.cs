// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

// Loaded alongside the instrumented library in a collectible load context.
// Only BCL types cross that boundary. This contains no test discovery methods.
public static class JxlFuzzTarget
{
    private static readonly Configuration Configuration = CreateConfiguration();

    public static string Run(byte[] bytes, int operation, bool forwardOnly, CancellationToken cancellationToken)
    {
        using Stream input = forwardOnly ? new JxlTestStream(bytes, 31) : new MemoryStream(bytes, false);
        JxlDecoder decoder = new()
        {
            MaxPixels = 262144, MaxTotalPixels = 524288, MaxDecodedPixels = 1048576,
            MaxFrames = 2, MaxCodedFrames = 16, MaxModularBytes = 16 << 20, MaxMetadataBytes = 1 << 20,
        };
        try
        {
            switch (operation)
            {
                case 0:
                    return new JxlImageFormatDetector().DetectFormat(bytes) == null ? "unknown" : "jxl";
                case 1:
                    IImageInfo info = decoder.Identify(Configuration, input, cancellationToken);
                    return $"identify:{info.Width}:{info.Height}:{info.PixelType.BitsPerPixel}:{info.Metadata.GetJxlMetadata().FrameCount}";
                case 2:
                    using (Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration, input, cancellationToken))
                    {
                        ulong hash = 14695981039346656037;
                        foreach (ImageFrame<Rgba32> frame in image.Frames)
                        for (int y = 0; y < frame.Height; y++)
                        foreach (Rgba32 pixel in frame.PixelBuffer.DangerousGetRowSpan(y))
                        { hash = unchecked((hash ^ pixel.PackedValue) * 1099511628211); }
                        return $"pixels:{image.Width}:{image.Height}:{image.Frames.Count}:{hash:x16}";
                    }
                case 3:
                    using (JxlRawImage image = decoder.DecodeRaw(Configuration, input, cancellationToken))
                    {
                        ulong hash = 14695981039346656037;
                        foreach (JxlRawFrame frame in image.Frames)
                        foreach (JxlRawPlane plane in frame.Planes)
                        for (int y = 0; y < plane.Height; y++)
                        {
                            if (plane.SampleType == JxlSampleType.Integer)
                            { foreach (int value in plane.GetIntegerRowSpan(y)) { hash = unchecked((hash ^ (uint)value) * 1099511628211); } }
                            else
                            { foreach (float value in plane.GetFloatRowSpan(y)) { hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211); } }
                        }
                        return $"raw:{image.Width}:{image.Height}:{image.Frames.Count}:{hash:x16}";
                    }
                case 4:
                    using (BoundedOutput output = new())
                    {
                        JxlJpegTranscoder.JxlToJpeg(Configuration, input, output, new JxlJpegReconstructionOptions
                        {
                            MaxPixels = 65536, MaxMetadataBytes = 1 << 20, MaxModularBytes = 16 << 20,
                            MaxScans = 16, MaxScanBlocks = 1048576, MaxOutputBytes = 2 << 20,
                        }, cancellationToken);
                        return Hash("jpeg", output);
                    }
                case 5:
                    using (BoundedOutput output = new())
                    {
                        JxlJpegTranscoder.JpegToJxl(Configuration, input, output, new()
                        { MaxPixels = 65536, MaxInputBytes = 32768, MaxScans = 16, Effort = 1 }, cancellationToken);
                        return Hash("encoded", output);
                    }
                case 6:
                    using (JxlCodestreamStream codestream = new(input, 1 << 20, false, cancellationToken))
                    using (BoundedOutput output = new())
                    {
                        codestream.CopyTo(output);
                        return Hash("container", output);
                    }
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
        catch (InvalidImageContentException) { return "invalid"; }
        // The internal stream reports truncation directly; public APIs wrap it.
        catch (EndOfStreamException) when (operation == 6) { return "invalid"; }
        catch (NotSupportedException) { return "unsupported"; }
        catch (InvalidMemoryOperationException) when (operation == 5) { return "encoder-limit"; }
    }

    private static string Hash(string kind, MemoryStream output)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in output.GetBuffer().AsSpan(0, checked((int)output.Length)))
        { hash = unchecked((hash ^ value) * 1099511628211); }
        return $"{kind}:{output.Length}:{hash:x16}";
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Imaging.Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        configuration.MemoryAllocator = MemoryAllocator.Create(new() { AllocationLimitMegabytes = 64, MaximumPoolSizeMegabytes = 0 });
        return configuration;
    }

    private sealed class BoundedOutput : MemoryStream
    {
        private void Check(int count)
        { if (count > (2 << 20) - this.Position) { throw new InvalidOperationException("Fuzz output exceeded two MiB."); } }
        public override void Write(byte[] buffer, int offset, int count) { this.Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { this.Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { this.Check(1); base.WriteByte(value); }
    }
}
