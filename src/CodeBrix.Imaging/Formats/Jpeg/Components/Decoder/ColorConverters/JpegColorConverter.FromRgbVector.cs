// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Decoder.ColorConverters; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Decoder.ColorConverters;

internal abstract partial class JpegColorConverterBase
{
    internal sealed class FromRgbVector : JpegColorConverterVector
    {
        public FromRgbVector(int precision)
            : base(JpegColorSpace.RGB, precision)
        {
        }

        protected override void ConvertCoreVectorizedInplace(in ComponentValues values)
        {
            ref var rBase =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component0));
            ref var gBase =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component1));
            ref var bBase =
                ref Unsafe.As<float, Vector<float>>(ref MemoryMarshal.GetReference(values.Component2));

            var scale = new Vector<float>(1 / this.MaximumValue);

            nint n = values.Component0.Length / Vector<float>.Count;
            for (nint i = 0; i < n; i++)
            {
                ref var r = ref Unsafe.Add(ref rBase, i);
                ref var g = ref Unsafe.Add(ref gBase, i);
                ref var b = ref Unsafe.Add(ref bBase, i);
                r *= scale;
                g *= scale;
                b *= scale;
            }
        }

        protected override void ConvertCoreInplace(in ComponentValues values) =>
            FromRgbScalar.ConvertCoreInplace(values, this.MaximumValue);
    }
}