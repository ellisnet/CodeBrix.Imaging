// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

// Deliberately lazy: constructing the fixture or discovering tests never accesses
// the external cache. Every resource request passes the gate before any file I/O.
internal sealed class JxlReferenceFixture
{
    internal async Task<string> GetInputAsync(string name, CancellationToken cancellationToken)
    {
        JxlExternalResources.RequireEnabled();
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(JxlHeaderTests.ResourceRoot, "manifest.json"), cancellationToken));
        foreach (JsonElement item in manifest.RootElement.EnumerateArray())
        {
            if (item.GetProperty("case").GetString() != name) { continue; }
            string baseName = item.GetProperty("base").GetString();
            if (!item.GetProperty("external").GetBoolean()) { return Path.Combine(JxlHeaderTests.ResourceRoot, baseName, "input.jxl"); }
            System.Uri uri = new($"https://raw.githubusercontent.com/libjxl/conformance/{JxlExternalResources.ConformanceRevision}/testcases/{baseName}/input.jxl");
            return await JxlExternalResources.GetAsync(uri, item.GetProperty("inputSha256").GetString(), cancellationToken);
        }
        throw new System.InvalidOperationException("Conformance case is missing: " + name);
    }

    internal async Task<string> GetAsync(string name, string resource, CancellationToken cancellationToken)
    {
        JxlExternalResources.RequireEnabled();
        using JsonDocument description = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(JxlHeaderTests.ResourceRoot, name, "test.json"), cancellationToken));
        string hash = description.RootElement.GetProperty("sha256sums").GetProperty(resource).GetString();
        return await JxlExternalResources.GetAsync(JxlExternalResources.ReferenceUri(hash), hash, cancellationToken);
    }

    internal async Task<JxlNpyImage> OpenImageAsync(string name, string resource, CancellationToken cancellationToken)
    {
        string path = await this.GetAsync(name, resource, cancellationToken);
        return JxlNpyImage.Open(path);
    }
}
