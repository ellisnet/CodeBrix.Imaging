// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Advanced; //Was previously: namespace SixLabors.ImageSharp.Advanced;

/// <summary>
/// Defines the contract for an action that operates on a row interval.
/// </summary>
public interface IRowIntervalOperation
{
    /// <summary>
    /// Invokes the method passing the row interval.
    /// </summary>
    /// <param name="rows">The row interval.</param>
    void Invoke(in RowInterval rows);
}