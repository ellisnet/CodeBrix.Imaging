// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl.Color;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

internal static class JxlPixelReference
{
    internal static async Task VerifyAsync(string name)
    {
        JxlExternalResources.RequireEnabled();
        JxlReferenceFixture fixture = new();
        string inputPath = await fixture.GetInputAsync(name, TestContext.Current.CancellationToken);
        using JxlNpyImage reference = await fixture.OpenImageAsync(name, "reference_image.npy", TestContext.Current.CancellationToken);
        using Image<RgbaVector> image = Image.Load<RgbaVector>(inputPath);
        using FileStream headerInput = File.OpenRead(inputPath);
        using JxlCodestreamStream headerStream = new(headerInput, 64 << 20, true, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(new JxlBitReader(headerStream, TestContext.Current.CancellationToken), 100_000_000, 64 << 20);
        int alphaIndex = Array.FindIndex(header.ExtraChannels, e => e.Type == 0);
        bool associated = alphaIndex >= 0 && header.ExtraChannels[alphaIndex].AlphaAssociated;
        IccColorProfile referenceColor = null;
        JxlEnumeratedColorSpace referenceEncoding = null;
        bool referenceSrgb = !header.Color.WantIcc && !header.Color.HasGamma && header.Color.TransferFunction == 13 && header.Color.Primaries == 1 && header.Color.WhitePoint == 1;
        if (header.XybEncoded && !referenceSrgb)
        {
            if (!header.Color.WantIcc) { referenceEncoding = new(header.Color, header.IntensityTarget); }
            else
            {
                string profilePath = await fixture.GetAsync(name, "reference.icc", TestContext.Current.CancellationToken);
                referenceColor = IccColorProfile.Read(await File.ReadAllBytesAsync(profilePath, TestContext.Current.CancellationToken));
                referenceColor.ValidateDestination();
            }
        }
        Assert.Equal(reference.Frames, image.Frames.Count);
        Assert.True(reference.Channels <= 4, $"Reference requires raw channels: {reference.Channels}");
        using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(JxlHeaderTests.ResourceRoot, name, "test.json")));
        for (int frameIndex = 0; frameIndex < reference.Frames; frameIndex++)
        {
            float[] expected = reference.ReadFrame(frameIndex);
            Assert.Equal(image.Width, reference.Width);
            Assert.Equal(image.Height, reference.Height);
            double squared = 0;
            float peak = 0;
            for (int y = 0; y < reference.Height; y++)
            {
                for (int x = 0; x < reference.Width; x++)
                {
                    Vector4 pixel = image.Frames[frameIndex][x, y].ToScaledVector4();
                    if (referenceColor != null) { pixel = ToReferenceColor(pixel, referenceColor); }
                    if (referenceEncoding != null)
                    {
                        // Enumerated BT.709 extends its linear segment below zero.
                        // Generic ICC curves use a different signed extension.
                        Vector3 linear = new(JxlEnumeratedColorSpace.LinearSrgb(pixel.X), JxlEnumeratedColorSpace.LinearSrgb(pixel.Y), JxlEnumeratedColorSpace.LinearSrgb(pixel.Z));
                        Vector3 original = referenceEncoding.FromLinearSrgb(linear);
                        pixel = new(original, pixel.W);
                    }
                    // The conformance planes retain associated alpha; public pixel types expose straight RGB.
                    if (associated && pixel.W > 0) { pixel.X *= pixel.W; pixel.Y *= pixel.W; pixel.Z *= pixel.W; }
                    for (int c = 0; c < reference.Channels; c++)
                    {
                        float actual = reference.Channels <= 2 ? c == 0 ? pixel.X : pixel.W
                            : c switch { 0 => pixel.X, 1 => pixel.Y, 2 => pixel.Z, _ => pixel.W };
                        float difference = Math.Abs(actual - expected[(((y * reference.Width) + x) * reference.Channels) + c]);
                        Assert.True(float.IsFinite(difference));
                        squared += difference * difference;
                        peak = Math.Max(peak, difference);
                    }
                }
            }
            JsonElement limits = settings.RootElement.GetProperty("frames")[frameIndex];
            Assert.Equal(limits.GetProperty("name").GetString(), image.Frames[frameIndex].Metadata.GetJxlMetadata().Name);
            if (limits.TryGetProperty("duration", out JsonElement duration))
            {
                JxlMetadata metadata = image.Metadata.GetJxlMetadata();
                double seconds = (double)image.Frames[frameIndex].Metadata.GetJxlMetadata().Duration * metadata.TicksPerSecondDenominator / metadata.TicksPerSecondNumerator;
                Assert.Equal(duration.GetDouble(), seconds, 6);
            }
            double rms = Math.Sqrt(squared / expected.Length);
            Assert.True(peak <= limits.GetProperty("peak_error").GetDouble() && rms <= limits.GetProperty("rms_error").GetDouble(),
                $"{name} frame {frameIndex}: peak={peak}, RMS={rms}");
        }
    }

    private static Vector4 ToReferenceColor(Vector4 value, IccColorProfile destination)
    {
        Span<float> channels = stackalloc float[4] { value.X, value.Y, value.Z, value.W };
        Vector3 xyz = IccColorProfile.Srgb.ToXyz(channels);
        destination.FromXyz(xyz, channels);
        return destination.Channels == 1 ? new(channels[0], channels[0], channels[0], value.W)
            : new(channels[0], channels[1], channels[2], value.W);
    }
}
