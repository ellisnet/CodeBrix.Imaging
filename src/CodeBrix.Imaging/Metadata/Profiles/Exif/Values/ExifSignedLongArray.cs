// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Metadata.Profiles.Exif; //Was previously: namespace SixLabors.ImageSharp.Metadata.Profiles.Exif;

internal sealed class ExifSignedLongArray : ExifArrayValue<int>
{
    public ExifSignedLongArray(ExifTagValue tag)
        : base(tag)
    {
    }

    private ExifSignedLongArray(ExifSignedLongArray value)
        : base(value)
    {
    }

    public override ExifDataType DataType => ExifDataType.SignedLong;

    public override IExifValue DeepClone() => new ExifSignedLongArray(this);
}