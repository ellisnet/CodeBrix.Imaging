// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Imaging.Common.Helpers;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.Metadata.Profiles.Exif;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.Metadata.Profiles.Iptc;
using CodeBrix.Imaging.Metadata.Profiles.Xmp;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// The decoder metadata creator.
/// </summary>
internal static class TiffDecoderMetadataCreator
{
    public static ImageMetadata Create<TPixel>(List<ImageFrame<TPixel>> frames, bool ignoreMetadata, ByteOrder byteOrder, bool isBigTiff)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (frames.Count < 1)
        {
            TiffThrowHelper.ThrowImageFormatException("Expected at least one frame.");
        }

        var imageMetaData = Create(byteOrder, isBigTiff, frames[0].Metadata.ExifProfile);

        if (!ignoreMetadata)
        {
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var frameMetaData = frame.Metadata;
                if (TryGetIptc(frameMetaData.ExifProfile.Values, out var iptcBytes))
                {
                    frameMetaData.IptcProfile = new IptcProfile(iptcBytes);
                }

                var xmpProfileBytes = frameMetaData.ExifProfile.GetValue(ExifTag.XMP);
                if (xmpProfileBytes != null)
                {
                    frameMetaData.XmpProfile = new XmpProfile(xmpProfileBytes.Value);
                }

                var iccProfileBytes = frameMetaData.ExifProfile.GetValue(ExifTag.IccProfile);
                if (iccProfileBytes != null)
                {
                    frameMetaData.IccProfile = new IccProfile(iccProfileBytes.Value);
                }
            }
        }

        return imageMetaData;
    }

    public static ImageMetadata Create(ByteOrder byteOrder, bool isBigTiff, ExifProfile exifProfile)
    {
        var imageMetaData = new ImageMetadata(TiffFormat.Instance);
        SetResolution(imageMetaData, exifProfile);

        var tiffMetadata = imageMetaData.GetTiffMetadata();
        tiffMetadata.ByteOrder = byteOrder;
        tiffMetadata.FormatType = isBigTiff ? TiffFormatType.BigTIFF : TiffFormatType.Default;

        return imageMetaData;
    }

    private static void SetResolution(ImageMetadata imageMetaData, ExifProfile exifProfile)
    {
        imageMetaData.ResolutionUnits = exifProfile != null ? UnitConverter.ExifProfileToResolutionUnit(exifProfile) : PixelResolutionUnit.PixelsPerInch;
        var horizontalResolution = exifProfile?.GetValue(ExifTag.XResolution)?.Value.ToDouble();
        if (horizontalResolution != null)
        {
            imageMetaData.HorizontalResolution = horizontalResolution.Value;
        }

        var verticalResolution = exifProfile?.GetValue(ExifTag.YResolution)?.Value.ToDouble();
        if (verticalResolution != null)
        {
            imageMetaData.VerticalResolution = verticalResolution.Value;
        }
    }

    private static bool TryGetIptc(IReadOnlyList<IExifValue> exifValues, out byte[] iptcBytes)
    {
        iptcBytes = null;
        var iptc = exifValues.FirstOrDefault(f => f.Tag == ExifTag.IPTC);

        if (iptc != null)
        {
            if (iptc.DataType == ExifDataType.Byte || iptc.DataType == ExifDataType.Undefined)
            {
                iptcBytes = (byte[])iptc.GetValue();
                return true;
            }

            // Some Encoders write the data type of IPTC as long.
            if (iptc.DataType == ExifDataType.Long)
            {
                var iptcValues = (uint[])iptc.GetValue();
                iptcBytes = new byte[iptcValues.Length * 4];
                Buffer.BlockCopy(iptcValues, 0, iptcBytes, 0, iptcValues.Length * 4);
                if (iptcBytes[0] == 0x1c)
                {
                    return true;
                }
                else if (iptcBytes[3] != 0x1c)
                {
                    return false;
                }

                // Probably wrong endianess, swap byte order.
                var iptcBytesSpan = iptcBytes.AsSpan();
                Span<byte> buffer = stackalloc byte[4];
                for (var i = 0; i < iptcBytes.Length; i += 4)
                {
                    iptcBytesSpan.Slice(i, 4).CopyTo(buffer);
                    iptcBytes[i] = buffer[3];
                    iptcBytes[i + 1] = buffer[2];
                    iptcBytes[i + 2] = buffer[1];
                    iptcBytes[i + 3] = buffer[0];
                }

                return true;
            }
        }

        return false;
    }
}