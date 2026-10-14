// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRawTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void thirty_one_bit_integer_samples_do_not_round_through_float(bool coalesce)
    {
        string path = Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "integer31.jxl");
        using FileStream stream = File.OpenRead(path);
        using JxlRawImage image = new JxlDecoder { Coalesce = coalesce }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
        JxlRawPlane plane = Assert.Single(Assert.Single(image.Frames).Planes);
        Assert.Equal(JxlSampleType.Integer, plane.SampleType);
        Assert.Equal(31, plane.BitsPerSample);
        Assert.Equal(new[] { 1_073_741_825, int.MaxValue }, plane.GetIntegerRowSpan(0).ToArray());
        Assert.Throws<InvalidOperationException>(() => plane.GetFloatRowSpan(0));
    }

    [Fact]
    public void uncropped_layers_preserve_integer_samples_and_blending_metadata()
    {
        using FileStream source = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, "sunset_logo", "input.jxl"));
        using JxlRawImage image = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, source, TestContext.Current.CancellationToken);
        Assert.False(image.IsCoalesced);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(7, image.Metadata.GetJxlMetadata().Orientation);
        foreach (JxlRawFrame frame in image.Frames)
        {
            Assert.Equal(2048, frame.Width); Assert.Equal(1024, frame.Height);
            Assert.Equal(-662, frame.Metadata.CropLeft); Assert.Equal(-100, frame.Metadata.CropTop);
            Assert.Equal(4, frame.Planes.Count);
            Assert.All(frame.Planes, plane => Assert.Equal(JxlSampleType.Integer, plane.SampleType));
        }
        Assert.Equal(JxlBlendMode.Blend, image.Frames[1].Metadata.Blending.Mode);
        Assert.Equal(1, image.Frames[1].Metadata.Blending.SourceReference);
        Assert.Equal(JxlExtraChannelType.Alpha, image.Frames[0].Planes[3].ExtraChannelType);
        JxlRawPlane retained = image.Frames[0].Planes[0];
        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => retained.GetIntegerRowSpan(0));
        Assert.True(source.CanRead);
    }

    [Theory]
    [InlineData("spot")]
    [InlineData("cmyk_layers")]
    [InlineData("lossless_pfm")]
    [Trait("Category", "JxlConformance")]
    public async Task all_raw_channels_match_external_reference(string name)
    { await VerifyRawAsync(name); }

    internal static async Task VerifyRawAsync(string name)
    {
        JxlExternalResources.RequireEnabled();
        using JxlNpyImage reference = await new JxlReferenceFixture().OpenImageAsync(name, "reference_image.npy", TestContext.Current.CancellationToken);
        string path = await new JxlReferenceFixture().GetInputAsync(name, TestContext.Current.CancellationToken);
        using FileStream source = File.OpenRead(path);
        using JxlRawImage image = new JxlDecoder().DecodeRaw(Configuration.Default, source, TestContext.Current.CancellationToken);
        using JsonDocument description = JsonDocument.Parse(File.ReadAllText(Path.Combine(JxlHeaderTests.ResourceRoot, name, "test.json")));
        Assert.Equal(reference.Frames, image.Frames.Count);
        for (int f = 0; f < reference.Frames; f++)
        {
            JxlRawFrame frame = image.Frames[f];
            Assert.Equal(reference.Channels, frame.Planes.Count);
            Assert.Equal(reference.Width, frame.Width); Assert.Equal(reference.Height, frame.Height);
            float[] expected = reference.ReadFrame(f);
            double squared = 0;
            float peak = 0;
            for (int y = 0; y < reference.Height; y++)
            {
                for (int x = 0; x < reference.Width; x++)
                {
                    for (int c = 0; c < reference.Channels; c++)
                    {
                        JxlRawPlane plane = frame.Planes[c];
                        float actual = plane.SampleType == JxlSampleType.FloatingPoint ? plane.GetFloatRowSpan(y)[x]
                            : plane.GetIntegerRowSpan(y)[x] / (float)((1UL << plane.BitsPerSample) - 1);
                        float difference = Math.Abs(actual - expected[(((y * reference.Width) + x) * reference.Channels) + c]);
                        Assert.True(float.IsFinite(difference));
                        squared += difference * difference;
                        peak = Math.Max(peak, difference);
                    }
                }
            }
            JsonElement limits = description.RootElement.GetProperty("frames")[f];
            double rms = Math.Sqrt(squared / expected.Length);
            Assert.True(peak <= limits.GetProperty("peak_error").GetDouble() && rms <= limits.GetProperty("rms_error").GetDouble(),
                $"{name} frame {f}: peak={peak}, RMS={rms}");
        }
    }
}
