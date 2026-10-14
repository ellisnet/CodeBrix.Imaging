// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Default quantization parameters adapted from jxl-oxide crates/jxl-vardct/src/
// dequant.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Largest rectangular transform corrected from libjxl lib/jxl/quant_weights.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed partial class JxlDequantMatrices
{
    private static readonly float[] SequenceA = [-1.025f, -0.78f, -0.65012f, -0.19041574f, -0.20819396f, -0.421064f, -0.32733846f];
    private static readonly float[] SequenceB = [-0.30419582f, -0.36330363f, -0.3566038f, -0.34430745f, -0.33699593f, -0.30180866f, -0.27321684f];
    private static readonly float[] SequenceC = [-1.2f, -1.2f, -0.8f, -0.7f, -0.7f, -0.4f, -0.5f];
    private static readonly float[][] DefaultDct4 = [
        [2200.0f, 0.0f, 0.0f, 0.0f],
        [392.0f, 0.0f, 0.0f, 0.0f],
        [112.0f, -0.25f, -0.25f, -0.5f],
    ];
    private static readonly float[][] DefaultDct4x8 = [
        [2198.0505f, -0.96269625f, -0.7619425f, -0.65511405f],
        [764.36554f, -0.926302f, -0.967523f, -0.2784529f],
        [527.10754f, -1.4594386f, -1.4500821f, -1.5843723f],
    ];
    private static readonly float[][] DefaultHornuss = [
                [280.0f, 3160.0f, 3160.0f],
                [60.0f, 864.0f, 864.0f],
                [18.0f, 200.0f, 200.0f],
            ];
    private static readonly float[][] DefaultDct2 = [
                [3840.0f, 2560.0f, 1280.0f, 640.0f, 480.0f, 300.0f],
                [960.0f, 640.0f, 320.0f, 180.0f, 140.0f, 120.0f],
                [640.0f, 320.0f, 128.0f, 64.0f, 32.0f, 16.0f],
            ];
    private static readonly float[][] DefaultAfv = [
                    [3072.0f, 3072.0f, 256.0f, 256.0f, 256.0f, 414.0f, 0.0f, 0.0f, 0.0f],
                    [1024.0f, 1024.0f, 50.0f, 50.0f, 50.0f, 58.0f, 0.0f, 0.0f, 0.0f],
                    [384.0f, 384.0f, 12.0f, 12.0f, 12.0f, 22.0f, -0.25f, -0.25f, -0.25f],
                ];
    private static float[][] DefaultDct(int index) => index switch
    {
        0 => [
                [3150.0f, 0.0f, -0.4f, -0.4f, -0.4f, -2.0f],
                [560.0f, 0.0f, -0.3f, -0.3f, -0.3f, -0.3f],
                [512.0f, -2.0f, -1.0f, 0.0f, -1.0f, -2.0f],
            ],
        4 => [
                [8996.873f, -1.3000778f, -0.4942453f, -0.43909377f, -0.6350102f, -0.9017726f, -1.6162099f],
                [3191.4836f, -0.67424583f, -0.80745816f, -0.4492584f, -0.3586544f, -0.3132239f, -0.37615025f],
                [1157.504f, -2.0531423f, -1.4f, -0.5068713f, -0.4270873f, -1.4856834f, -4.920914f],
            ],
        5 => [
                [15718.408f, -1.025f, -0.98f, -0.9012f, -0.4f, -0.48819396f, -0.421064f, -0.27f],
                [7305.7637f, -0.8041958f, -0.76330364f, -0.5566038f, -0.49785304f, -0.43699592f, -0.40180868f, -0.27321684f],
                [3803.5317f, -3.0607336f, -2.041327f, -2.023565f, -0.54953897f, -0.4f, -0.4f, -0.3f],
            ],
        6 => [
                [7240.7734f, -0.7f, -0.7f, -0.2f, -0.2f, -0.2f, -0.5f],
                [1448.1547f, -0.5f, -0.5f, -0.5f, -0.2f, -0.2f, -0.2f],
                [506.85413f, -1.4f, -0.2f, -0.5f, -0.5f, -1.5f, -3.6f],
            ],
        7 => [
                [16283.249f, -1.7812846f, -1.6309059f, -1.0382179f, -0.85f, -0.7f, -0.9f, -1.2360638f],
                [5089.1577f, -0.3200494f, -0.3536285f, -0.3034f, -0.61f, -0.5f, -0.5f, -0.6f],
                [3397.7761f, -0.32132736f, -0.3450762f, -0.7034f, -0.9f, -1.0f, -1.0f, -1.1754606f],
            ],
        8 => [
                [13844.971f, -0.971138f, -0.658f, -0.42026f, -0.22712f, -0.2206f, -0.226f, -0.6f],
                [4798.964f, -0.6112531f, -0.8377079f, -0.7901486f, -0.26927274f, -0.38272768f, -0.22924222f, -0.20719099f],
                [1807.2369f, -1.2f, -1.2f, -0.7f, -0.7f, -0.7f, -0.4f, -0.5f],
            ],
        11 => Common(23966.166f, 8380.191f, 4493.024f),
        12 => Common(15358.898f, 5597.3604f, 2919.9617f),
        13 => Common(47932.332f, 16760.383f, 8986.048f),
        14 => Common(30717.797f, 11194.721f, 5839.9233f),
        15 => Common(95864.664f, 33520.766f, 17972.096f),
        // Correct the upstream oxide constants against libjxl quant_weights.cc.
        16 => Common(61435.594f, 22389.441f, 11679.847f),
        _ => throw new System.ArgumentOutOfRangeException(nameof(index)),
    };
    private static float[][] Common(float a, float b, float c) =>
        [[a, ..SequenceA], [b, ..SequenceB], [c, ..SequenceC]];
}
