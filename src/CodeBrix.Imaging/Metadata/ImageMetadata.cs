// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Metadata.Profiles.Exif;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.Metadata.Profiles.Iptc;
using CodeBrix.Imaging.Metadata.Profiles.Xmp;
using System;
using System.Collections.Generic;

namespace CodeBrix.Imaging.Metadata; //Was previously: namespace SixLabors.ImageSharp.Metadata;

/// <summary>
/// Encapsulates the metadata of an image.
/// </summary>
public sealed class ImageMetadata : IDeepCloneable<ImageMetadata>
{
    /// <summary>
    /// The default horizontal resolution value (dots per inch) in x direction.
    /// <remarks>The default value is 96 <see cref="PixelResolutionUnit.PixelsPerInch"/>.</remarks>
    /// </summary>
    public const double DefaultHorizontalResolution = 96;

    /// <summary>
    /// The default vertical resolution value (dots per inch) in y direction.
    /// <remarks>The default value is 96 <see cref="PixelResolutionUnit.PixelsPerInch"/>.</remarks>
    /// </summary>
    public const double DefaultVerticalResolution = 96;

    /// <summary>
    /// The default pixel resolution units.
    /// <remarks>The default value is <see cref="PixelResolutionUnit.PixelsPerInch"/>.</remarks>
    /// </summary>
    public const PixelResolutionUnit DefaultPixelResolutionUnits = PixelResolutionUnit.PixelsPerInch;

    private readonly Dictionary<IImageFormat, IDeepCloneable> formatMetadata = new Dictionary<IImageFormat, IDeepCloneable>();
    private double horizontalResolution;
    private double verticalResolution;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageMetadata"/> class.
    /// </summary>
    public ImageMetadata(IImageFormat expectedFormat = null)
    {
        this.horizontalResolution = DefaultHorizontalResolution;
        this.verticalResolution = DefaultVerticalResolution;
        this.ResolutionUnits = DefaultPixelResolutionUnits;
        this.ExpectedFormat = expectedFormat ?? UnknownImageFormat.Instance;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageMetadata"/> class
    /// by making a copy from other metadata.
    /// </summary>
    /// <param name="other">
    /// The other <see cref="ImageMetadata"/> to create this instance from.
    /// </param>
    private ImageMetadata(ImageMetadata other)
    {
        this.HorizontalResolution = other.HorizontalResolution;
        this.VerticalResolution = other.VerticalResolution;
        this.ResolutionUnits = other.ResolutionUnits;

        foreach (var meta in other.formatMetadata)
        {
            this.formatMetadata.Add(meta.Key, meta.Value.DeepClone());
        }

        this.ExifProfile = other.ExifProfile?.DeepClone();
        this.IccProfile = other.IccProfile?.DeepClone();
        this.IptcProfile = other.IptcProfile?.DeepClone();
        this.XmpProfile = other.XmpProfile?.DeepClone();
        this.ExpectedFormat = other.ExpectedFormat;
    }

    /// <summary>
    /// Gets or sets the resolution of the image in x- direction.
    /// It is defined as the number of dots per <see cref="ResolutionUnits"/> and should be an positive value.
    /// </summary>
    /// <value>The density of the image in x- direction.</value>
    public double HorizontalResolution
    {
        get => this.horizontalResolution;

        set
        {
            if (value > 0)
            {
                this.horizontalResolution = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the resolution of the image in y- direction.
    /// It is defined as the number of dots per <see cref="ResolutionUnits"/> and should be an positive value.
    /// </summary>
    /// <value>The density of the image in y- direction.</value>
    public double VerticalResolution
    {
        get => this.verticalResolution;

        set
        {
            if (value > 0)
            {
                this.verticalResolution = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets unit of measure used when reporting resolution.
    /// <list type="table">
    ///   <listheader>
    ///     <term>Value</term>
    ///     <description>Unit</description>
    ///   </listheader>
    ///   <item>
    ///     <term>AspectRatio (00)</term>
    ///     <description>No units; width:height pixel aspect ratio = Ydensity:Xdensity</description>
    ///   </item>
    ///   <item>
    ///     <term>PixelsPerInch (01)</term>
    ///     <description>Pixels per inch (2.54 cm)</description>
    ///   </item>
    ///   <item>
    ///     <term>PixelsPerCentimeter (02)</term>
    ///     <description>Pixels per centimeter</description>
    ///   </item>
    ///   <item>
    ///     <term>PixelsPerMeter (03)</term>
    ///     <description>Pixels per meter (100 cm)</description>
    ///   </item>
    /// </list>
    /// </summary>
    public PixelResolutionUnit ResolutionUnits { get; set; }

    /// <summary>
    /// Gets or sets the Exif profile.
    /// </summary>
    public ExifProfile ExifProfile { get; set; }

    /// <summary>
    /// Gets or sets the XMP profile.
    /// </summary>
    public XmpProfile XmpProfile { get; set; }

    /// <summary>
    /// Gets or sets the list of ICC profiles.
    /// </summary>
    public IccProfile IccProfile { get; set; }

    /// <summary>
    /// Gets or sets the IPTC profile.
    /// </summary>
    public IptcProfile IptcProfile { get; set; }

    /// <summary>
    /// Gets or sets the image format this image is expected to be encoded in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the format the image was decoded from. An image created in memory rather than
    /// loaded - for example <c>new Image&lt;Rgba32&gt;(width, height)</c> - starts out as
    /// <see cref="UnknownImageFormat"/>, which has no encoder: saving such an image without
    /// naming a format throws <see cref="System.NotSupportedException"/>.
    /// </para>
    /// <para>
    /// NOTE: saving an image UPDATES this property to the format that was just written. So
    /// <c>image.SaveAsJpeg(stream)</c> leaves <see cref="ExpectedFormat"/> reporting JPEG even
    /// if the image was originally decoded from a PNG. Two consequences worth knowing:
    /// saving is not a read-only operation on the image, and saving the same image
    /// concurrently to two different formats races on this property. Clone the image first if
    /// you need to write several formats in parallel.
    /// </para>
    /// <para>
    /// The specialised <c>ExportAs8bppGrayscaleBmpFormat</c> helpers deliberately do NOT
    /// update this property, because that output cannot be round-tripped through the normal
    /// encoder pipeline.
    /// </para>
    /// </remarks>
    public IImageFormat ExpectedFormat { get; set; }

    /// <summary>
    /// Gets the metadata value associated with the specified key.
    /// </summary>
    /// <typeparam name="TFormatMetadata">The type of metadata.</typeparam>
    /// <param name="key">The key of the value to get.</param>
    /// <returns>
    /// The <typeparamref name="TFormatMetadata"/>.
    /// </returns>
    public TFormatMetadata GetFormatMetadata<TFormatMetadata>(IImageFormat<TFormatMetadata> key)
        where TFormatMetadata : class, IDeepCloneable
    {
        if (this.formatMetadata.TryGetValue(key, out var meta))
        {
            return (TFormatMetadata)meta;
        }

        var newMeta = key.CreateDefaultFormatMetadata();
        this.formatMetadata[key] = newMeta;
        return newMeta;
    }

    /// <inheritdoc/>
    public ImageMetadata DeepClone() => new(this);

    /// <summary>
    /// Synchronizes the profiles with the current metadata.
    /// </summary>
    internal void SyncProfiles() => this.ExifProfile?.Sync(this);
}