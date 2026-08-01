// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.IO;

namespace CodeBrix.Imaging.IO; //Was previously: namespace SixLabors.ImageSharp.IO;

/// <summary>
/// A wrapper around the local File apis.
/// </summary>
/// <remarks>
/// Paths are passed through to <see cref="File"/> unmodified and unvalidated. Deciding
/// whether a path is safe to open or create is the caller's responsibility - this library
/// has no way to know which directories an application considers legitimate.
/// <para>
/// A previous revision attempted a path-traversal guard here. It was removed because it
/// could not work: <see cref="Path.GetFullPath(string)"/> normalizes ".." segments away
/// before any such check runs, so the guard blocked no traversal at all, while rejecting
/// legitimate file names that end in dots (valid on Linux and macOS). Callers that need
/// containment should compare <see cref="Path.GetFullPath(string)"/> of the candidate
/// against their own allowed root before calling into this library.
/// </para>
/// </remarks>
internal sealed class LocalFileSystem : IFileSystem
{
    /// <inheritdoc/>
    public Stream OpenRead(string path) => File.OpenRead(path);

    /// <inheritdoc/>
    public Stream Create(string path) => File.Create(path);
}