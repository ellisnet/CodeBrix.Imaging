// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Registers JPEG XL detection, decoding, and encoding.</summary>
public sealed class JxlConfigurationModule : IConfigurationModule
{
    /// <inheritdoc/>
    public void Configure(Configuration configuration)
    {
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.SetEncoder(JxlFormat.Instance, new JxlEncoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
    }
}
