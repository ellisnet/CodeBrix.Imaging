// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.Imaging.Formats.Jxl;

internal static class JxlThrowHelper
{
    [DoesNotReturn]
    internal static void Invalid(string message) => throw new InvalidImageContentException("Invalid JPEG XL: " + message);
}
