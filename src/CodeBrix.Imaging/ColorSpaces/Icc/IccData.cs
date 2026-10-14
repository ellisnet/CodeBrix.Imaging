// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// ICC field validation adapted from skcms skcms.cc,
// 7e572ada698a60ff50bd9038fd6e01e44f40868a (BSD-3-Clause).
// Copyright (c) 2018 Google Inc. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the complete upstream notice.

using System;
using System.Buffers.Binary;

namespace CodeBrix.Imaging.ColorSpaces.Icc;

internal static class IccData
{
    internal static void Require(ReadOnlySpan<byte> data, int offset, int size)
    {
        if (offset < 0 || size < 0 || offset > data.Length - size) { throw Invalid("field exceeds its data block."); }
    }
    internal static uint U32(ReadOnlySpan<byte> data, int offset)
    { Require(data, offset, 4); return BinaryPrimitives.ReadUInt32BigEndian(data[offset..]); }
    internal static ushort U16(ReadOnlySpan<byte> data, int offset)
    { Require(data, offset, 2); return BinaryPrimitives.ReadUInt16BigEndian(data[offset..]); }
    internal static float Fixed(ReadOnlySpan<byte> data, int offset) => unchecked((int)U32(data, offset)) / 65536f;
    internal static int Offset(ReadOnlySpan<byte> data, int offset)
    {
        uint value = U32(data, offset);
        if (value > int.MaxValue) { throw Invalid("offset or length exceeds the supported profile size."); }
        return (int)value;
    }
    internal static InvalidImageContentException Invalid(string message) => new("Invalid ICC profile: " + message);
}
