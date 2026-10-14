// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging;

public static partial class MetadataExtensions
{
    /// <summary>Gets the JPEG XL metadata associated with an image.</summary>
    /// <param name="metadata">The image metadata.</param>
    /// <returns>The JPEG XL metadata.</returns>
    public static JxlMetadata GetJxlMetadata(this ImageMetadata metadata) => metadata.GetFormatMetadata(JxlFormat.Instance);

    /// <summary>Gets the JPEG XL metadata associated with a frame.</summary>
    /// <param name="metadata">The frame metadata.</param>
    /// <returns>The JPEG XL frame metadata.</returns>
    public static JxlFrameMetadata GetJxlMetadata(this ImageFrameMetadata metadata) => metadata.GetFormatMetadata(JxlFormat.Instance);
}
