// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlExternalReferenceTests
{
    [Fact]
    [Trait("Category", "JxlConformance")]
    public async Task fetches_verified_gcs_reference_array_and_profile()
    {
        JxlExternalResources.RequireEnabled();
        JxlReferenceFixture fixture = new();
        using JxlNpyImage reference = await fixture.OpenImageAsync("grayscale", "reference_image.npy", TestContext.Current.CancellationToken);
        Assert.Equal(1, reference.Frames);
        Assert.Equal(200, reference.Width);
        Assert.Equal(200, reference.Height);
        Assert.Equal(1, reference.Channels);
        float[] samples = reference.ReadFrame(0);
        Assert.Equal(40000, samples.Length);
        Assert.All(samples, value => Assert.True(float.IsFinite(value)));
        string profile = await fixture.GetAsync("grayscale", "reference.icc", TestContext.Current.CancellationToken);
        byte[] data = await File.ReadAllBytesAsync(profile, TestContext.Current.CancellationToken);
        Assert.True(data.AsSpan(36, 4).SequenceEqual("acsp"u8));
    }
}
