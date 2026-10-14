// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlVarDctTests
{
    [Theory]
    [InlineData("animation_spline")]
    [InlineData("noise")]
    [InlineData("alpha_premultiplied")]
    [InlineData("animation_icos4d")]
    [InlineData("bench_oriented_brg")]
    [InlineData("grayscale")]
    [InlineData("grayscale_jpeg")]
    [InlineData("opsin_inverse")]
    [InlineData("upsampling")]
    [InlineData("patches")]
    [Trait("Category", "JxlConformance")]
    public async Task vardct_samples_match_external_float_reference(string name)
    {
        await JxlPixelReference.VerifyAsync(name);
    }

    [Theory]
    [InlineData("progressive")]
    [InlineData("cafe")]
    [Trait("Category", "JxlConformance")]
    public async Task large_vardct_samples_match_external_float_reference(string name)
    {
        await JxlPixelReference.VerifyAsync(name);
    }

    [Fact]
    [Trait("Category", "JxlConformance")]
    public async Task bt709_vardct_matches_external_float_reference()
    {
        await JxlPixelReference.VerifyAsync("bike");
    }
}
