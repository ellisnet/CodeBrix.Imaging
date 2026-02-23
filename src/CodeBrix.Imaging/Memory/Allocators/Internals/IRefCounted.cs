// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Memory.Internals; //Was previously: namespace SixLabors.ImageSharp.Memory.Internals;

/// <summary>
/// Defines an common interface for ref-counted objects.
/// </summary>
internal interface IRefCounted
{
    /// <summary>
    /// Increments the reference counter.
    /// </summary>
    void AddRef();

    /// <summary>
    /// Decrements the reference counter.
    /// </summary>
    void ReleaseRef();
}