// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Describes the JPEG XL image format.</summary>
public sealed class JxlFormat : IImageFormat<JxlMetadata, JxlFrameMetadata>
{
    /// <summary>The display name of the JPEG XL format.</summary>
    public const string FormatName = "JPEG XL";
    /// <summary>The MIME type of JPEG XL images.</summary>
    public const string FormatMimeType = "image/jxl";
    /// <summary>The default file extension, including its leading period.</summary>
    public const string FormatDefaultExtension = ".jxl";

    private JxlFormat() { }

    /// <summary>Gets the JPEG XL format descriptor.</summary>
    public static JxlFormat Instance { get; } = new();
    /// <inheritdoc/>
    public string Name => FormatName;
    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;
    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;
    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => JxlConstants.MimeTypes;
    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => JxlConstants.FileExtensions;
    /// <inheritdoc/>
    public JxlMetadata CreateDefaultFormatMetadata() => new();
    /// <inheritdoc/>
    public JxlFrameMetadata CreateDefaultFormatFrameMetadata() => new();
}
