// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlFullConformanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        // Test discovery reads only the repository manifest, never the external cache.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(JxlHeaderTests.ResourceRoot, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.EnumerateArray())
        { yield return [item.GetProperty("case").GetString(), item.GetProperty("level").GetInt32()]; }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "JxlFullConformance")]
    public async Task all_level_records_match_reference_pixels_and_jpeg_bytes(string name, int level)
    {
        JxlExternalResources.RequireEnabled();
        Assert.True(level is 5 or 10);
        // This upstream case publishes only a rounded PNG, no float NPY.
        if (name == "mul_no_extra_channels") { JxlModularTests.VerifyMultiplication(); }
        else if (name is "spot" or "cmyk_layers" or "lossless_pfm") { await JxlRawTests.VerifyRawAsync(name); }
        else { await JxlPixelReference.VerifyAsync(name); }
        using JsonDocument description = JsonDocument.Parse(File.ReadAllText(Path.Combine(JxlHeaderTests.ResourceRoot, name, "test.json")));
        if (description.RootElement.TryGetProperty("reconstructed_jpeg", out _))
        { await JxlJpegReconstructionTests.VerifyJpegAsync(name); }
    }
}
