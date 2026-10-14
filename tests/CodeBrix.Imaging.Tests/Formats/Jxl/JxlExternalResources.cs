// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

internal static class JxlExternalResources
{
    internal const string EnableVariable = "CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS";
    internal const string SkipReason = "Set " + EnableVariable + "=1 to run JPEG XL tests that access external resources or download reference data.";
    internal const string ConformanceRevision = "b1d0f990b03e57bf6d137c365cd5dc8b470b9191";

    internal static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    internal static void RequireEnabled() => Assert.SkipUnless(Enabled, SkipReason);

    internal static async Task<string> GetAsync(Uri uri, string sha256, CancellationToken cancellationToken)
    {
        // This check precedes even the cache-path lookup. Cached data is external too.
        RequireEnabled();
        if (sha256.Length != 64 || !IsHex(sha256)) { throw new ArgumentException("Invalid SHA-256.", nameof(sha256)); }
        string cache = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_REFERENCE_CACHE");
        if (string.IsNullOrWhiteSpace(cache)) { cache = Path.Combine(Path.GetTempPath(), "codebrix-jxl-references"); }
        string destination = Path.Combine(cache, sha256);
        if (File.Exists(destination))
        {
            await VerifyAsync(destination, sha256, cancellationToken);
            return destination;
        }

        Directory.CreateDirectory(cache);
        string temporary = Path.Combine(cache, sha256 + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(15) };
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream output = File.Create(temporary))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            await VerifyAsync(temporary, sha256, cancellationToken);
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    internal static Uri ReferenceUri(string hash) => new("https://storage.googleapis.com/jxl-conformance/objects/" + hash);

    private static async Task VerifyAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using FileStream file = File.OpenRead(path);
        string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        Assert.Equal(expected, actual);
    }

    private static bool IsHex(string value)
    {
        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character)) { return false; }
        }

        return true;
    }
}
