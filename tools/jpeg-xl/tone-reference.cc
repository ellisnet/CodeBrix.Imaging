// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original test-only wrapper calling the pinned libjxl scalar tone mapper.
#include "lib/jxl/cms/tone_mapping.h"
#include <cstring>
#include <fstream>
#include <iostream>

int main(int argc, char** argv) {
    const char* gate = std::getenv("CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS");
    if (!gate || std::strcmp(gate, "1") != 0) {
        std::cerr << "Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to use native references.\n";
        return 2;
    }
    if (argc != 6) {
        std::cerr << "Usage: tone-reference pq|hlg source-nits target-nits linear-rgb.f32 output-rgb.f32\n";
        return 2;
    }
    try {
        const float source = std::stof(argv[2]), target = std::stof(argv[3]);
        const jxl::Vector3 luminances = {.212639f, .7151687f, .0721923f};
        jxl::Rec2408ToneMapperBase pq({0, source}, {0, target}, luminances);
        jxl::HlgOOTF_Base hlg(source, target, luminances);
        std::ifstream input(argv[4], std::ios::binary);
        std::ofstream output(argv[5], std::ios::binary);
        if (!input || !output) throw std::runtime_error("Cannot open reference streams");
        jxl::Color rgb;
        while (input.read(reinterpret_cast<char*>(rgb.data()), sizeof(float) * 3)) {
            if (std::strcmp(argv[1], "pq") == 0) pq.ToneMap(rgb);
            else hlg.Apply(rgb);
            jxl::GamutMapScalar(rgb, luminances);
            for (float& v : rgb) {
                double a = std::abs(v);
                v = std::copysign(a <= .0031308 ? 12.92 * a : 1.055 * std::pow(a, 1.0 / 2.4) - .055, v);
            }
            output.write(reinterpret_cast<const char*>(rgb.data()), sizeof(float) * 3);
        }
        if (!input.eof() || input.gcount() != 0 || !output) throw std::runtime_error("Incomplete reference vector");
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
