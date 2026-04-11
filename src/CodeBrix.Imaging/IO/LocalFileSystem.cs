// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace CodeBrix.Imaging.IO; //Was previously: namespace SixLabors.ImageSharp.IO;

/// <summary>
/// A wrapper around the local File apis.
/// </summary>
internal sealed class LocalFileSystem : IFileSystem
{
    /// <inheritdoc/>
    public Stream OpenRead(string path)
    {
        ValidatePath(path);
        return File.OpenRead(path);
    }

    /// <inheritdoc/>
    public Stream Create(string path)
    {
        ValidatePath(path);
        return File.Create(path);
    }

    /// <summary>
    /// Validates the file path to prevent path traversal attacks and other malicious input.
    /// </summary>
    /// <param name="path">The file path to validate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, whitespace, or contains path traversal sequences.</exception>
    private static void ValidatePath(string path)
    {
        Guard.NotNullOrWhiteSpace(path, nameof(path));

        // Reject paths containing path traversal sequences.
        // Check both the raw path and the normalized path to catch encoded or
        // OS-specific separator variants.
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Contains(".." + Path.DirectorySeparatorChar)
            || fullPath.Contains(".." + Path.AltDirectorySeparatorChar)
            || fullPath.EndsWith(".."))
        {
            throw new ArgumentException("Path contains invalid traversal sequence.", nameof(path));
        }
    }
}