// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Effort-11 candidate configurations and decoder-speed restrictions adapted
// from libjxl lib/jxl/enc_frame.cc (TectonicPlateSettingsLessPalette/MorePalette
// and EncodeFrame), enc_modular.cc and enc_ans.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal sealed record JxlModularSearchOptions(uint GroupShift = 3, int PaletteColors = 0, int LocalPalettePercent = 80,
    int GlobalPalettePercent = 95, float Sampling = 1, int Predictor = 15, bool NoWeighted = false, bool Patches = true, int MaximumHistograms = 256)
{
    // Decoder-speed restrictions follow enc_modular.cc and enc_ans.cc at the
    // same revision. Negative sampling/palette percentages retain effort defaults.
    internal static JxlModularSearchOptions ForDecodingSpeed(JxlModularSearchOptions original, int speed, int effort, uint groupShift)
    {
        if (speed == 0) { return original; }
        JxlModularSearchOptions result = original ?? new(GroupShift: groupShift,
            PaletteColors: effort <= 3 ? 256 : effort <= 7 ? 1024 : 4096, LocalPalettePercent: -1,
            GlobalPalettePercent: -1, Sampling: -1, Predictor: effort < 10 ? 14 : 15);
        return result with
        {
            GroupShift = speed >= 2 ? 0 : result.GroupShift, NoWeighted = true,
            Predictor = speed >= 3 || result.Predictor == 6 ? 5 : result.Predictor,
            Sampling = speed >= 4 ? 0 : result.Sampling, MaximumHistograms = speed >= 2 ? 12 : 256,
        };
    }

    internal static JxlModularSearchOptions[] Probes =>
        [new(), new(PaletteColors: 70000, Sampling: .01f, Predictor: 0, NoWeighted: true, Patches: false)];

    internal static IEnumerable<JxlModularSearchOptions> Candidates(bool morePalette)
    {
        JxlModularSearchOptions p = new(GroupShift: 0, PaletteColors: morePalette ? 70000 : 1024,
            LocalPalettePercent: morePalette ? 80 : 0);
        yield return p;
        if (!morePalette)
        {
            p = p with { LocalPalettePercent = 80, GroupShift = 1, PaletteColors = 0, GlobalPalettePercent = 0 }; yield return p;
            p = p with { GlobalPalettePercent = 95, GroupShift = 2 }; yield return p;
            p = p with { GroupShift = 3, Patches = false, NoWeighted = true }; yield return p;
            p = p with { PaletteColors = 1024, NoWeighted = false }; yield return p;
            p = p with { Patches = true, NoWeighted = true }; yield return p;
            p = p with { NoWeighted = false, GlobalPalettePercent = 0 }; yield return p;
            p = p with { GlobalPalettePercent = 95, Sampling = .9f, GroupShift = 2 }; yield return p;
            p = p with { GroupShift = 3, PaletteColors = 0, NoWeighted = true }; yield return p;
            p = p with { NoWeighted = false, GlobalPalettePercent = 0 }; yield return p;
            p = p with { PaletteColors = 1024, Sampling = .95f, GroupShift = 1, LocalPalettePercent = 0 }; yield return p;
            p = p with { GroupShift = 2, PaletteColors = 0 }; yield return p;
            p = p with { LocalPalettePercent = 80, NoWeighted = true }; yield return p;
            p = p with { PaletteColors = 1024, GlobalPalettePercent = 95, NoWeighted = false, GroupShift = 3 }; yield return p;
            p = p with { PaletteColors = 0, Patches = false }; yield return p;
            p = p with { Patches = true, NoWeighted = true }; yield return p;
            p = p with { PaletteColors = 1024, Patches = false }; yield return p;
            p = p with { Sampling = .5f, Patches = true, NoWeighted = false }; yield return p;
            p = p with { Predictor = 0, Sampling = 0, LocalPalettePercent = 0, GlobalPalettePercent = 0, Patches = false }; yield return p;
            p = p with { LocalPalettePercent = 80, GlobalPalettePercent = 95, Sampling = 1, PaletteColors = 0 }; yield return p;
            p = p with { Patches = true, Predictor = 14 }; yield return p;
            p = p with { Sampling = .9f, Patches = false }; yield return p;
            p = p with { PaletteColors = 1024, Patches = true, Predictor = 6, Sampling = 1 }; yield return p;
            p = p with { Sampling = .95f, GroupShift = 2, PaletteColors = 0, GlobalPalettePercent = 0 }; yield return p;
        }
        else
        {
            p = p with { GroupShift = 2, LocalPalettePercent = 0, Patches = false, NoWeighted = true }; yield return p;
            p = p with { LocalPalettePercent = 80, NoWeighted = false, GroupShift = 3 }; yield return p;
            p = p with { Sampling = .9f }; yield return p;
            p = p with { Patches = true, Sampling = .95f, GroupShift = 0 }; yield return p;
            p = p with { GroupShift = 3 }; yield return p;
            p = p with { Patches = false, NoWeighted = true }; yield return p;
            p = p with { Sampling = .5f }; yield return p;
            p = p with { NoWeighted = false, Predictor = 0, Sampling = 0 }; yield return p;
            p = p with { Patches = true, GlobalPalettePercent = 0 }; yield return p;
            p = p with { Sampling = .01f, PaletteColors = 0, Patches = false, NoWeighted = true }; yield return p;
            p = p with { GlobalPalettePercent = 95, NoWeighted = false, PaletteColors = 70000 }; yield return p;
            p = p with { Sampling = 1, GroupShift = 0, LocalPalettePercent = 0, GlobalPalettePercent = 0, NoWeighted = true }; yield return p;
            p = p with { GlobalPalettePercent = 95, GroupShift = 1 }; yield return p;
            p = p with { GroupShift = 2 }; yield return p;
            p = p with { LocalPalettePercent = 80, NoWeighted = false, GroupShift = 3 }; yield return p;
            p = p with { Sampling = .5f, GroupShift = 1, LocalPalettePercent = 0, NoWeighted = true }; yield return p;
            p = p with { NoWeighted = false, GroupShift = 2 }; yield return p;
            p = p with { LocalPalettePercent = 80, GroupShift = 3, NoWeighted = true }; yield return p;
            p = p with { NoWeighted = false, Predictor = 4, Sampling = 1 }; yield return p;
        }
    }
}
