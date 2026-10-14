// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Jpeg.Components;
using CodeBrix.Imaging.Formats.Jpeg.Components.Decoder;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.IO;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JpegCoefficientRegressionTests
{
    [Theory]
    [InlineData("baseline-444")] [InlineData("baseline-420")] [InlineData("gray")]
    [InlineData("progressive-444")] [InlineData("progressive-420")]
    [InlineData("progressive-restart")] [InlineData("sequential-multiscan")]
    [InlineData("restart-optimized")] [InlineData("rgb-progressive")]
    public void existing_jpeg_decoder_matches_reconstruction_parser_coefficients(string name)
    {
        string path = Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Jpeg", name + ".jpg");
        byte[] bytes = File.ReadAllBytes(path);
        using JxlJpegParser reference = JxlJpegParser.Read(bytes, Configuration.Default.MemoryAllocator, 100_000_000, TestContext.Current.CancellationToken);
        using MemoryStream source = new(bytes, false); using BufferedReadStream buffered = new(Configuration.Default, source);
        Capture converter = new(reference.Coefficients);
        HuffmanScanDecoder scan = new(buffered, converter, TestContext.Current.CancellationToken);
        using JpegDecoderCore decoder = new(Configuration.Default, new JpegDecoder());
        decoder.ParseStream(buffered, scan, TestContext.Current.CancellationToken);
        converter.VerifyDeferred();
    }

    private sealed class Capture(JxlJpegCoefficients expected) : SpectralConverter
    {
        private JpegFrame frame;
        private int mcuRow;

        public override void InjectFrameData(JpegFrame frame, IRawJpegData jpegData) => this.frame = frame;

        public override void ConvertStrideBaseline()
        {
            for (int c = 0; c < this.frame.Components.Length; c++)
            {
                JpegComponent component = this.frame.Components[c];
                for (int y = 0; y < component.SpectralBlocks.Height; y++)
                {
                    int by = this.mcuRow * component.VerticalSamplingFactor + y;
                    if (by < component.HeightInBlocks) { this.VerifyRow(c, y, by); }
                    component.SpectralBlocks.DangerousGetRowSpan(y).Clear();
                }
            }
            this.mcuRow++;
        }

        internal void VerifyDeferred()
        {
            if (this.Converted) { return; }
            for (int c = 0; c < this.frame.Components.Length; c++)
            for (int y = 0; y < this.frame.Components[c].HeightInBlocks; y++) { this.VerifyRow(c, y, y); }
        }

        private void VerifyRow(int component, int storageRow, int by)
        {
            JpegComponent channel = this.frame.Components[component];
            JxlModularPlane plane = expected.Planes[expected.ComponentPlane(component)];
            Span<Block8x8> row = channel.SpectralBlocks.DangerousGetRowSpan(storageRow);
            for (int bx = 0; bx < channel.WidthInBlocks; bx++)
            for (int k = 0; k < 64; k++)
            {
                int value = plane.Row(by * 8 + k / 8)[bx * 8 + k % 8];
                // The existing decoder stores coefficients transposed for IDCT.
                short actual = row[bx][k % 8 * 8 + k / 8];
                Assert.True(value == actual, $"Component {component}, block ({bx},{by}), coefficient {k}: expected {value}, actual {actual}.");
            }
        }
    }
}
