// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Decoder.ColorConverters; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Decoder.ColorConverters;

internal abstract partial class JpegColorConverterBase
{
    internal sealed class FromCmykScalar : JpegColorConverterScalar
    {
        public FromCmykScalar(int precision)
            : base(JpegColorSpace.Cmyk, precision)
        {
        }

        public override void ConvertToRgbInplace(in ComponentValues values) =>
            ConvertCoreInplace(values, this.MaximumValue);

        internal static void ConvertCoreInplace(in ComponentValues values, float maxValue)
        {
            var c0 = values.Component0;
            var c1 = values.Component1;
            var c2 = values.Component2;
            var c3 = values.Component3;

            var scale = 1 / (maxValue * maxValue);
            for (var i = 0; i < c0.Length; i++)
            {
                var c = c0[i];
                var m = c1[i];
                var y = c2[i];
                var k = c3[i];

                k *= scale;
                c0[i] = c * k;
                c1[i] = m * k;
                c2[i] = y * k;
            }
        }
    }
}