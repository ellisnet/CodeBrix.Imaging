// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

internal static class JxlNativeReference
{
    internal static string Find(string tool)
    {
        JxlExternalResources.RequireEnabled();
        string directory = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_NATIVE_TOOLS");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory),
            "Set CODEBRIX_IMAGING_JXL_NATIVE_TOOLS to the absolute directory of your pinned reference tools.");
        string path = Path.Combine(directory, OperatingSystem.IsWindows() ? tool + ".exe" : tool);
        Assert.SkipUnless(File.Exists(path), "Build the pinned JPEG XL reference tools and set CODEBRIX_IMAGING_JXL_NATIVE_TOOLS.");
        return path;
    }

    internal static async Task<string> RunAsync(string tool, string[] arguments, CancellationToken cancellationToken)
    {
        JxlExternalResources.RequireEnabled();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ProcessStartInfo start = new(Find(tool)) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start);
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token), error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string log = await output + await error;
            Assert.True(process.ExitCode == 0, $"{tool} exited {process.ExitCode}: {log}");
            return log;
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
}
