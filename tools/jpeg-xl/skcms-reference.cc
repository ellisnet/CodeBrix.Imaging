// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Test-only wrapper around the pinned, BSD-3-Clause skcms reference. Never shipped.
#include "skcms.h"
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>

static std::vector<char> read(const char* path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) throw std::runtime_error("Cannot open reference input");
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}
int main(int argc, char** argv) {
    const char* gate = std::getenv("CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS");
    if (!gate || std::strcmp(gate, "1") != 0) {
        std::cerr << "Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to use native references.\n";
        return 2;
    }
    if (argc != 5) {
        std::cerr << "Usage: skcms-reference source.icc|srgb target.icc|srgb rgba.f32 output.f32\n";
        return 2;
    }
    try {
        std::vector<char> profiles[2]; skcms_ICCProfile parsed[2];
        const skcms_ICCProfile* profile[2];
        const int priority[] = {1, 0, 2};
        for (int i = 0; i < 2; i++) {
            if (std::strcmp(argv[1 + i], "srgb") == 0) { profile[i] = skcms_sRGB_profile(); continue; }
            profiles[i] = read(argv[1 + i]);
            if (!skcms_ParseWithA2BPriority(profiles[i].data(), profiles[i].size(), priority, 3, &parsed[i])) {
                throw std::runtime_error("Reference ICC parse failed");
            }
            profile[i] = &parsed[i];
        }
        auto input = read(argv[3]);
        if (input.size() % (4 * sizeof(float)) != 0) throw std::runtime_error("Input must contain RGBA float samples");
        std::vector<float> output(input.size() / sizeof(float));
        auto transform = [&]() { return skcms_Transform(input.data(), skcms_PixelFormat_RGBA_ffff, skcms_AlphaFormat_Unpremul, profile[0],
                output.data(), skcms_PixelFormat_RGBA_ffff, skcms_AlphaFormat_Unpremul, profile[1], output.size() / 4); };
        if (!transform() && (profiles[1].empty() || !skcms_MakeUsableAsDestination(&parsed[1]) || !transform())) {
            throw std::runtime_error("Reference transform failed");
        }
        std::ofstream destination(argv[4], std::ios::binary);
        destination.write(reinterpret_cast<const char*>(output.data()), output.size() * sizeof(float));
        if (!destination) throw std::runtime_error("Reference output write failed");
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
