// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-modular/src/transform.rs and
// transform/rct.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d.
// Apache-2.0 option.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal abstract class JxlModularTransform
{
    internal abstract void Prepare(List<JxlModularPlane> channels, ref int metaChannels, JxlModularWorkspace workspace);
    internal abstract void Undo(List<JxlModularPlane> channels, int bitDepth, JxlModularWorkspace workspace);

    internal static JxlModularTransform Read(JxlBitReader reader, JxlWeightedPredictorHeader weighted) => reader.ReadBits(2) switch
    {
        0 => new JxlRctTransform(ReadChannel(reader), (int)reader.ReadU32(6, U(2), U(4, 2), U(6, 10))),
        1 => JxlPaletteTransform.ReadPalette(reader, weighted),
        2 => JxlSqueezeTransform.ReadSqueeze(reader),
        _ => throw new InvalidImageContentException("JPEG XL: invalid Modular transform."),
    };

    internal static int ReadChannel(JxlBitReader reader) => (int)reader.ReadU32(U(3), U(6, 8), U(10, 72), U(13, 1096));

    internal static void ValidateRange(List<JxlModularPlane> channels, int begin, int count)
    {
        if (begin < 0 || count < 1 || begin > channels.Count - count) { JxlThrowHelper.Invalid("invalid Modular transform channel range."); }
        JxlModularPlane first = channels[begin];
        for (int i = begin + 1; i < begin + count; i++)
        {
            if (channels[i].Width != first.Width || channels[i].Height != first.Height)
            { JxlThrowHelper.Invalid("Modular transform channels have different dimensions."); }
        }
    }
}

internal sealed class JxlRctTransform(int begin, int type) : JxlModularTransform
{
    internal override void Prepare(List<JxlModularPlane> channels, ref int metaChannels, JxlModularWorkspace workspace)
    {
        ValidateRange(channels, begin, 3);
        if (type > 41) { JxlThrowHelper.Invalid("invalid reversible color transform."); }
    }

    internal override void Undo(List<JxlModularPlane> channels, int bitDepth, JxlModularWorkspace workspace)
    {
        JxlModularPlane a = channels[begin], b = channels[begin + 1], c = channels[begin + 2];
        int transform = type % 7, permutation = type / 7;
        for (int y = 0; y < a.Height; y++)
        {
            Span<int> ar = a.Row(y), br = b.Row(y), cr = c.Row(y);
            for (int x = 0; x < a.Width; x++)
            {
                int av = ar[x], bv = br[x], cv = cr[x], d, e, f;
                unchecked
                {
                    if (transform == 6)
                    {
                        int temporary = av - (cv >> 1);
                        e = cv + temporary;
                        f = temporary - (bv >> 1);
                        d = f + bv;
                    }
                    else
                    {
                        d = av;
                        f = (transform & 1) != 0 ? cv + av : cv;
                        e = (transform >> 1) switch { 1 => bv + av, 2 => bv + ((av + f) >> 1), _ => bv };
                    }
                }
                (ar[x], br[x], cr[x]) = permutation switch
                {
                    1 => (f, d, e), 2 => (e, f, d), 3 => (d, f, e), 4 => (e, d, f), 5 => (f, e, d), _ => (d, e, f),
                };
            }
        }
    }
}
