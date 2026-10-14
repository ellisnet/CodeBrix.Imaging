// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original integration of the existing JPEG sample converters and managed ICC
// engine. Their source and license notices are retained in THIRD-PARTY-NOTICES.txt.

using System;
using CodeBrix.Imaging.ColorSpaces.Icc;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Decoder.ColorConverters;

internal abstract partial class JpegColorConverterBase
{
    internal sealed class FromCmykIcc(JpegColorSpace colorSpace, int precision, IccColorProfile profile)
        : JpegColorConverterScalar(colorSpace, precision)
    {
        public override void ConvertToRgbInplace(in ComponentValues values)
        {
            bool ycck = this.ColorSpace == JpegColorSpace.Ycck;
            if (ycck) { FromYCbCrScalar.ConvertCoreInplace(values, this.MaximumValue, this.HalfValue); }
            float scale = 1 / this.MaximumValue;
            Span<float> cmyk = stackalloc float[4], rgb = stackalloc float[3];
            for (int i = 0; i < values.Component0.Length; i++)
            {
                // JPEG's CMYK samples are inverted. YCCK's first three planes
                // transform directly to conventional CMY; only K is inverted.
                cmyk[0] = Math.Clamp(ycck ? values.Component0[i] : 1 - values.Component0[i] * scale, 0, 1);
                cmyk[1] = Math.Clamp(ycck ? values.Component1[i] : 1 - values.Component1[i] * scale, 0, 1);
                cmyk[2] = Math.Clamp(ycck ? values.Component2[i] : 1 - values.Component2[i] * scale, 0, 1);
                cmyk[3] = Math.Clamp(1 - values.Component3[i] * scale, 0, 1);
                IccColorProfile.Srgb.FromXyz(profile.ToXyz(cmyk), rgb);
                values.Component0[i] = rgb[0]; values.Component1[i] = rgb[1]; values.Component2[i] = rgb[2];
            }
        }
    }
}
