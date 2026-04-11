// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Decoder.ColorConverters; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Decoder.ColorConverters;

internal abstract partial class JpegColorConverterBase
{
    internal sealed class FromYccKVector : JpegColorConverterVector
    {
        public FromYccKVector(int precision)
            : base(JpegColorSpace.Ycck, precision)
        {
        }

        protected override void ConvertCoreVectorizedInplace(in ComponentValues values)
        {
            ref var c0Base =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component0));
            ref var c1Base =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component1));
            ref var c2Base =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component2));
            ref var kBase =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component3));

            var chromaOffset = new Vector<float>(-this.HalfValue);
            var scale = new Vector<float>(1 / (this.MaximumValue * this.MaximumValue));
            var max = new Vector<float>(this.MaximumValue);
            var rCrMult = new Vector<float>(FromYCbCrScalar.RCrMult);
            var gCbMult = new Vector<float>(-FromYCbCrScalar.GCbMult);
            var gCrMult = new Vector<float>(-FromYCbCrScalar.GCrMult);
            var bCbMult = new Vector<float>(FromYCbCrScalar.BCbMult);

            nint n = values.Component0.Length / Vector<float>.Count;
            for (nint i = 0; i < n; i++)
            {
                // y = yVals[i];
                // cb = cbVals[i] - 128F;
                // cr = crVals[i] - 128F;
                // k = kVals[i] / 256F;
                ref var c0 = ref Unsafe.Add(ref c0Base, i);
                ref var c1 = ref Unsafe.Add(ref c1Base, i);
                ref var c2 = ref Unsafe.Add(ref c2Base, i);

                var y = c0;
                var cb = c1 + chromaOffset;
                var cr = c2 + chromaOffset;
                var scaledK = Unsafe.Add(ref kBase, i) * scale;

                // r = y + (1.402F * cr);
                // g = y - (0.344136F * cb) - (0.714136F * cr);
                // b = y + (1.772F * cb);
                var r = y + (cr * rCrMult);
                var g = y + (cb * gCbMult) + (cr * gCrMult);
                var b = y + (cb * bCbMult);

                r = (max - r.FastRound()) * scaledK;
                g = (max - g.FastRound()) * scaledK;
                b = (max - b.FastRound()) * scaledK;

                c0 = r;
                c1 = g;
                c2 = b;
            }
        }

        protected override void ConvertCoreInplace(in ComponentValues values) =>
            FromYccKScalar.ConvertCoreInplace(values, this.MaximumValue, this.HalfValue);
    }
}