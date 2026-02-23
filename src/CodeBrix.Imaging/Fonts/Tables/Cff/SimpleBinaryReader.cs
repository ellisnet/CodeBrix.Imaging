// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Fonts;

namespace CodeBrix.Imaging.Fonts.Tables.Cff;

//was previously: namespace SixLabors.Fonts.Tables.Cff;

internal ref struct SimpleBinaryReader
{
    private readonly ReadOnlySpan<byte> buffer;

    public SimpleBinaryReader(ReadOnlySpan<byte> buffer)
    {
        this.buffer = buffer;
        Position = 0;
    }

    public int Length => buffer.Length;

    public int Position { get; set; }

    public bool CanRead()
    {
        return (uint)Position < buffer.Length;
    }

    public bool CanRead(int count)
    {
        return (uint)(Position + count) <= buffer.Length;
    }

    private void EnsureCanRead(int count)
    {
        if (!CanRead(count))
        {
            throw new InvalidFontFileException($"Attempted to read {count} byte(s) at position {Position}, but buffer length is {buffer.Length}.");
        }
    }

    public byte ReadByte()
    {
        EnsureCanRead(1);
        return buffer[Position++];
    }

    public int ReadInt16BE()
    {
        EnsureCanRead(2);
        var b1 = buffer[Position + 1];
        var b0 = buffer[Position];
        Position += 2;

        return (short)((b0 << 8) | b1);
    }

    public float ReadFloatFixed1616()
    {
        EnsureCanRead(4);
        // Read a BE int, we parse it later.
        var b3 = buffer[Position + 3];
        var b2 = buffer[Position + 2];
        var b1 = buffer[Position + 1];
        var b0 = buffer[Position];
        Position += 4;

        // This number is interpreted as a Fixed; that is, a signed number with 16 bits of fraction
        float number = (short)((b0 << 8) | b1);
        var fraction = (short)((b2 << 8) | b3) / 65536F;
        return number + fraction;
    }
}