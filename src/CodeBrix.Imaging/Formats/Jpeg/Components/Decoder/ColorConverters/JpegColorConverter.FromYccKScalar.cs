// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Decoder.ColorConverters; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Decoder.ColorConverters;

internal abstract partial class JpegColorConverterBase
{
    internal sealed class FromYccKScalar : JpegColorConverterScalar
    {
        public FromYccKScalar(int precision)
            : base(JpegColorSpace.Ycck, precision)
        {
        }

        public override void ConvertToRgbInplace(in ComponentValues values) =>
            ConvertCoreInplace(values, this.MaximumValue, this.HalfValue);

        internal static void ConvertCoreInplace(in ComponentValues values, float maxValue, float halfValue)
        {
            var c0 = values.Component0;
            var c1 = values.Component1;
            var c2 = values.Component2;
            var c3 = values.Component3;

            var scale = 1 / (maxValue * maxValue);

            for (var i = 0; i < values.Component0.Length; i++)
            {
                var y = c0[i];
                var cb = c1[i] - halfValue;
                var cr = c2[i] - halfValue;
                var scaledK = c3[i] * scale;

                c0[i] = (maxValue - MathF.Round(y + (1.402F * cr), MidpointRounding.AwayFromZero)) * scaledK;
                c1[i] = (maxValue - MathF.Round(y - (0.344136F * cb) - (0.714136F * cr), MidpointRounding.AwayFromZero)) * scaledK;
                c2[i] = (maxValue - MathF.Round(y + (1.772F * cb), MidpointRounding.AwayFromZero)) * scaledK;
            }
        }
    }
}