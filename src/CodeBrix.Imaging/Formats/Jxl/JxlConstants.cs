// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Jxl;

internal static class JxlConstants
{
    internal static readonly string[] FileExtensions = ["jxl"];
    internal static readonly string[] MimeTypes = ["image/jxl"];
    internal static ReadOnlySpan<byte> ContainerSignature => [0, 0, 0, 12, 74, 88, 76, 32, 13, 10, 135, 10];
}
