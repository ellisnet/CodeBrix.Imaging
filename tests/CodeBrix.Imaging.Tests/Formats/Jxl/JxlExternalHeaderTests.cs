// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlExternalHeaderTests
{
    [Theory]
    [InlineData("bike")]
    [InlineData("bike_5")]
    [InlineData("cafe")]
    [InlineData("cafe_5")]
    [Trait("Category", "JxlConformance")]
    public async Task identifies_external_conformance_inputs(string name)
    {
        JxlExternalResources.RequireEnabled();
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(JxlHeaderTests.ResourceRoot, "manifest.json"), TestContext.Current.CancellationToken));
        foreach (JsonElement item in manifest.RootElement.EnumerateArray())
        {
            if (item.GetProperty("case").GetString() != name) { continue; }
            // Level 5 entries are Git symlinks upstream; fetch the shared actual blob.
            string baseName = item.GetProperty("base").GetString();
            Uri uri = new($"https://raw.githubusercontent.com/libjxl/conformance/{JxlExternalResources.ConformanceRevision}/testcases/{baseName}/input.jxl");
            string path = await JxlExternalResources.GetAsync(uri, item.GetProperty("inputSha256").GetString(), TestContext.Current.CancellationToken);
            IImageInfo info = Image.Identify(path);
            Assert.Equal(item.GetProperty("width").GetInt32(), info.Width);
            Assert.Equal(item.GetProperty("height").GetInt32(), info.Height);
            Assert.Equal(item.GetProperty("bitsPerSample").GetInt32(), info.Metadata.GetJxlMetadata().BitsPerSample);
            Assert.Equal(item.GetProperty("exponentBitsPerSample").GetInt32(), info.Metadata.GetJxlMetadata().ExponentBitsPerSample);
            Assert.Equal(item.GetProperty("frames").GetInt32(), info.Metadata.GetJxlMetadata().FrameCount);
            return;
        }

        Assert.Fail("Case is missing from the pinned manifest: " + name);
    }

    [Fact]
    public async Task external_access_is_skipped_before_invalid_arguments_or_cache_access_when_not_enabled()
    {
        // No environment mutation: this invariant is checked in ordinary offline runs.
        Assert.SkipWhen(JxlExternalResources.Enabled, "This test verifies the default external-resource gate.");
        Exception exception = null;
        try { await JxlExternalResources.GetAsync(null, null, TestContext.Current.CancellationToken); }
        catch (Exception caught) { exception = caught; }
        Assert.NotNull(exception);
        Assert.StartsWith("$XunitDynamicSkip$", exception.Message);
    }
}
