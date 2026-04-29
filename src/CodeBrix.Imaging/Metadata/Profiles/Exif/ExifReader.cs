// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Metadata.Profiles.Exif; //Was previously: namespace SixLabors.ImageSharp.Metadata.Profiles.Exif;

internal class ExifReader : BaseExifReader
{
    public ExifReader(byte[] exifData)
        : this(exifData, null)
    {
    }

    public ExifReader(byte[] exifData, MemoryAllocator allocator)
        : base(new MemoryStream(exifData ?? throw new ArgumentNullException(nameof(exifData))), allocator)
    {
    }

    /// <summary>
    /// Reads and returns the collection of EXIF values.
    /// </summary>
    /// <returns>
    /// The <see cref="Collection{ExifValue}"/>.
    /// </returns>
    public List<IExifValue> ReadValues()
    {
        var values = new List<IExifValue>();

        // II == 0x4949
        this.IsBigEndian = this.ReadUInt16() != 0x4949;

        if (this.ReadUInt16() != 0x002A)
        {
            return values;
        }

        var ifdOffset = this.ReadUInt32();
        this.ReadValues(values, ifdOffset);

        var thumbnailOffset = this.ReadUInt32();
        this.GetThumbnail(thumbnailOffset);

        this.ReadSubIfd(values);

        this.ReadBigValues(values);

        return values;
    }

    private void GetThumbnail(uint offset)
    {
        if (offset == 0)
        {
            return;
        }

        var values = new List<IExifValue>();
        this.ReadValues(values, offset);

        foreach (ExifValue value in values)
        {
            if (value == ExifTag.JPEGInterchangeFormat)
            {
                this.ThumbnailOffset = ((ExifLong)value).Value;
            }
            else if (value == ExifTag.JPEGInterchangeFormatLength)
            {
                this.ThumbnailLength = ((ExifLong)value).Value;
            }
        }
    }
}

/// <summary>
/// Reads and parses EXIF data from a stream.
/// </summary>
internal abstract class BaseExifReader
{
    private readonly byte[] buf8 = new byte[8];
    private readonly byte[] buf4 = new byte[4];
    private readonly byte[] buf2 = new byte[2];

    private readonly MemoryAllocator allocator;
    private readonly Stream data;
    private List<ExifTag> invalidTags;

    private List<ulong> subIfds;

    protected BaseExifReader(Stream stream, MemoryAllocator allocator)
    {
        this.data = stream ?? throw new ArgumentNullException(nameof(stream));
        this.allocator = allocator;
    }

    private delegate TDataType ConverterMethod<TDataType>(ReadOnlySpan<byte> data);

    /// <summary>
    /// Gets the invalid tags.
    /// </summary>
    public IReadOnlyList<ExifTag> InvalidTags => this.invalidTags ?? (IReadOnlyList<ExifTag>)Array.Empty<ExifTag>();

    /// <summary>
    /// Gets or sets the thumbnail length in the byte stream.
    /// </summary>
    public uint ThumbnailLength { get; protected set; }

    /// <summary>
    /// Gets or sets the thumbnail offset position in the byte stream.
    /// </summary>
    public uint ThumbnailOffset { get; protected set; }

    public bool IsBigEndian { get; protected set; }

    public List<(ulong Offset, ExifDataType DataType, ulong NumberOfComponents, ExifValue Exif)> BigValues { get; } = new();

    protected void ReadBigValues(List<IExifValue> values)
    {
        if (this.BigValues.Count == 0)
        {
            return;
        }

        // Cap the maximum allowed big-value payload to the remaining stream length.
        // EXIF tags whose declared size exceeds Int32.MaxValue or the stream
        // length cannot possibly be valid and were previously only rejected by
        // a DEBUG-only assertion. Reject them at runtime so malformed input
        // cannot trigger an oversized allocation or overflow during the cast.
        var streamLength = (ulong)this.data.Length;
        var maxSize = 0;
        for (var i = this.BigValues.Count - 1; i >= 0; i--)
        {
            var tag = this.BigValues[i];
            var size = tag.NumberOfComponents * ExifDataTypes.GetSize(tag.DataType);

            if (size > int.MaxValue || size > streamLength)
            {
                this.AddInvalidTag(tag.Exif.Tag);
                this.BigValues.RemoveAt(i);
                continue;
            }

            if ((int)size > maxSize)
            {
                maxSize = (int)size;
            }
        }

        if (this.BigValues.Count == 0)
        {
            return;
        }

        if (this.allocator != null)
        {
            // tiff, bigTiff
            using var memory = this.allocator.Allocate<byte>(maxSize);
            var buf = memory.GetSpan();
            foreach (var tag in this.BigValues)
            {
                var size = tag.NumberOfComponents * ExifDataTypes.GetSize(tag.DataType);
                this.ReadBigValue(values, tag, buf.Slice(0, (int)size));
            }
        }
        else
        {
            // embedded exif
            var buf = maxSize <= 256 ? stackalloc byte[256] : new byte[maxSize];
            foreach (var tag in this.BigValues)
            {
                var size = tag.NumberOfComponents * ExifDataTypes.GetSize(tag.DataType);
                this.ReadBigValue(values, tag, buf.Slice(0, (int)size));
            }
        }

        this.BigValues.Clear();
    }

    /// <summary>
    /// Reads the values to the values collection.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <param name="offset">The IFD offset.</param>
    protected void ReadValues(List<IExifValue> values, uint offset)
    {
        if (offset > this.data.Length)
        {
            return;
        }

        this.Seek(offset);
        int count = this.ReadUInt16();

        Span<byte> offsetBuffer = stackalloc byte[4];
        for (var i = 0; i < count; i++)
        {
            this.ReadValue(values, offsetBuffer);
        }
    }

    protected void ReadSubIfd(List<IExifValue> values)
    {
        if (this.subIfds is not null)
        {
            foreach (var subIfdOffset in this.subIfds)
            {
                this.ReadValues(values, (uint)subIfdOffset);
            }
        }
    }

    protected void ReadValues64(List<IExifValue> values, ulong offset)
    {
        DebugGuard.MustBeLessThanOrEqualTo(offset, (ulong)this.data.Length, "By spec UInt64.MaxValue is supported, but .NET Stream.Length can Int64.MaxValue.");

        this.Seek(offset);
        var count = this.ReadUInt64();

        Span<byte> offsetBuffer = stackalloc byte[8];
        for (ulong i = 0; i < count; i++)
        {
            this.ReadValue64(values, offsetBuffer);
        }
    }

    protected void ReadBigValue(IList<IExifValue> values, (ulong Offset, ExifDataType DataType, ulong NumberOfComponents, ExifValue Exif) tag, Span<byte> buffer)
    {
        this.Seek(tag.Offset);
        if (this.TryReadSpan(buffer))
        {
            var value = this.ConvertValue(tag.DataType, buffer, tag.NumberOfComponents > 1 || tag.Exif.IsArray);
            this.Add(values, tag.Exif, value);
        }
    }

    private static TDataType[] ToArray<TDataType>(ExifDataType dataType, ReadOnlySpan<byte> data, ConverterMethod<TDataType> converter)
    {
        var dataTypeSize = (int)ExifDataTypes.GetSize(dataType);
        var length = data.Length / dataTypeSize;

        var result = new TDataType[length];

        for (var i = 0; i < length; i++)
        {
            var buffer = data.Slice(i * dataTypeSize, dataTypeSize);

            result.SetValue(converter(buffer), i);
        }

        return result;
    }

    private static string ConvertToString(Encoding encoding, ReadOnlySpan<byte> buffer)
    {
        var nullCharIndex = buffer.IndexOf((byte)0);

        if (nullCharIndex > -1)
        {
            buffer = buffer.Slice(0, nullCharIndex);
        }

        return encoding.GetString(buffer);
    }

    private byte ConvertToByte(ReadOnlySpan<byte> buffer) => buffer[0];

    private object ConvertValue(ExifDataType dataType, ReadOnlySpan<byte> buffer, bool isArray)
    {
        if (buffer.Length == 0)
        {
            return null;
        }

        switch (dataType)
        {
            case ExifDataType.Unknown:
                return null;
            case ExifDataType.Ascii:
                return ConvertToString(ExifConstants.DefaultEncoding, buffer);
            case ExifDataType.Byte:
            case ExifDataType.Undefined:
                if (!isArray)
                {
                    return this.ConvertToByte(buffer);
                }

                return buffer.ToArray();
            case ExifDataType.DoubleFloat:
                if (!isArray)
                {
                    return this.ConvertToDouble(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToDouble);
            case ExifDataType.Long:
            case ExifDataType.Ifd:
                if (!isArray)
                {
                    return this.ConvertToUInt32(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToUInt32);
            case ExifDataType.Rational:
                if (!isArray)
                {
                    return this.ToRational(buffer);
                }

                return ToArray(dataType, buffer, this.ToRational);
            case ExifDataType.Short:
                if (!isArray)
                {
                    return this.ConvertToShort(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToShort);
            case ExifDataType.SignedByte:
                if (!isArray)
                {
                    return this.ConvertToSignedByte(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToSignedByte);
            case ExifDataType.SignedLong:
                if (!isArray)
                {
                    return this.ConvertToInt32(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToInt32);
            case ExifDataType.SignedRational:
                if (!isArray)
                {
                    return this.ToSignedRational(buffer);
                }

                return ToArray(dataType, buffer, this.ToSignedRational);
            case ExifDataType.SignedShort:
                if (!isArray)
                {
                    return this.ConvertToSignedShort(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToSignedShort);
            case ExifDataType.SingleFloat:
                if (!isArray)
                {
                    return this.ConvertToSingle(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToSingle);
            case ExifDataType.Long8:
            case ExifDataType.Ifd8:
                if (!isArray)
                {
                    return this.ConvertToUInt64(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToUInt64);
            case ExifDataType.SignedLong8:
                if (!isArray)
                {
                    return this.ConvertToInt64(buffer);
                }

                return ToArray(dataType, buffer, this.ConvertToUInt64);

            default:
                throw new NotSupportedException($"Data type {dataType} is not supported.");
        }
    }

    private void ReadValue(List<IExifValue> values, Span<byte> offsetBuffer)
    {
        // 2   | 2    | 4     | 4
        // tag | type | count | value offset
        if ((this.data.Length - this.data.Position) < 12)
        {
            return;
        }

        var tag = (ExifTagValue)this.ReadUInt16();
        var dataType = EnumUtils.Parse(this.ReadUInt16(), ExifDataType.Unknown);

        var numberOfComponents = this.ReadUInt32();

        this.TryReadSpan(offsetBuffer);

        // Ensure that the data type is valid
        if (dataType == ExifDataType.Unknown)
        {
            return;
        }

        // Issue #132: ExifDataType == Undefined is treated like a byte array.
        // If numberOfComponents == 0 this value can only be handled as an inline value and must fallback to 4 (bytes)
        if (numberOfComponents == 0)
        {
            numberOfComponents = 4 / ExifDataTypes.GetSize(dataType);
        }

        var exifValue = ExifValues.Create(tag) ?? ExifValues.Create(tag, dataType, numberOfComponents);

        if (exifValue is null)
        {
            this.AddInvalidTag(new UnkownExifTag(tag));
            return;
        }

        var size = numberOfComponents * ExifDataTypes.GetSize(dataType);
        if (size > 4)
        {
            var newOffset = this.ConvertToUInt32(offsetBuffer);

            // Ensure that the new index does not overrun the data.
            if (newOffset > int.MaxValue || (newOffset + size) > this.data.Length)
            {
                this.AddInvalidTag(new UnkownExifTag(tag));
                return;
            }

            this.BigValues.Add((newOffset, dataType, numberOfComponents, exifValue));
        }
        else
        {
            var value = this.ConvertValue(dataType, offsetBuffer.Slice(0, (int)size), numberOfComponents > 1 || exifValue.IsArray);
            this.Add(values, exifValue, value);
        }
    }

    private void ReadValue64(List<IExifValue> values, Span<byte> offsetBuffer)
    {
        if ((this.data.Length - this.data.Position) < 20)
        {
            return;
        }

        var tag = (ExifTagValue)this.ReadUInt16();
        var dataType = EnumUtils.Parse(this.ReadUInt16(), ExifDataType.Unknown);

        var numberOfComponents = this.ReadUInt64();

        this.TryReadSpan(offsetBuffer);

        if (dataType == ExifDataType.Unknown)
        {
            return;
        }

        if (numberOfComponents == 0)
        {
            numberOfComponents = 8 / ExifDataTypes.GetSize(dataType);
        }

        // The StripOffsets, StripByteCounts, TileOffsets, and TileByteCounts tags are allowed to have the datatype TIFF_LONG8 in BigTIFF.
        // Old datatypes TIFF_LONG, and TIFF_SHORT where allowed in the TIFF 6.0 specification, are still valid in BigTIFF, too.
        // Likewise, tags that point to other IFDs, like e.g. the SubIFDs tag, are now allowed to have the datatype TIFF_IFD8 in BigTIFF.
        // Again, the old datatypes TIFF_IFD, and the hardly recommendable TIFF_LONG, are still valid, too.
        // https://www.awaresystems.be/imaging/tiff/bigtiff.html
        ExifValue exifValue;
        switch (tag)
        {
            case ExifTagValue.StripOffsets:
                exifValue = new ExifLong8Array(ExifTagValue.StripOffsets);
                break;
            case ExifTagValue.StripByteCounts:
                exifValue = new ExifLong8Array(ExifTagValue.StripByteCounts);
                break;
            case ExifTagValue.TileOffsets:
                exifValue = new ExifLong8Array(ExifTagValue.TileOffsets);
                break;
            case ExifTagValue.TileByteCounts:
                exifValue = new ExifLong8Array(ExifTagValue.TileByteCounts);
                break;
            default:
                exifValue = ExifValues.Create(tag) ?? ExifValues.Create(tag, dataType, numberOfComponents);
                break;
        }

        if (exifValue is null)
        {
            this.AddInvalidTag(new UnkownExifTag(tag));
            return;
        }

        var size = numberOfComponents * ExifDataTypes.GetSize(dataType);
        if (size > 8)
        {
            var newOffset = this.ConvertToUInt64(offsetBuffer);

            // Validate bounds without underflow:
            //  - The previous check `newOffset > ulong.MaxValue` was tautologically false.
            //  - `(ulong)this.data.Length - size` would underflow when size exceeds the stream length,
            //    silently producing a huge "valid" upper bound.
            // Also reject sizes that cannot be represented as Int64, since the underlying
            // Stream is bounded by Int64.MaxValue (and we cast to long when seeking).
            var streamLength = (ulong)this.data.Length;
            if (size > (ulong)long.MaxValue
                || size > streamLength
                || newOffset > streamLength - size)
            {
                this.AddInvalidTag(new UnkownExifTag(tag));
                return;
            }

            this.BigValues.Add((newOffset, dataType, numberOfComponents, exifValue));
        }
        else
        {
            var value = this.ConvertValue(dataType, offsetBuffer.Slice(0, (int)size), numberOfComponents > 1 || exifValue.IsArray);
            this.Add(values, exifValue, value);
        }
    }

    private void Add(IList<IExifValue> values, IExifValue exif, object value)
    {
        if (!exif.TrySetValue(value))
        {
            return;
        }

        foreach (var val in values)
        {
            // Sometimes duplicates appear, can compare val.Tag == exif.Tag
            if (val == exif)
            {
                Debug.WriteLine($"Duplicate Exif tag: tag={exif.Tag}, dataType={exif.DataType}");
                return;
            }
        }

        if (exif.Tag == ExifTag.SubIFDOffset)
        {
            this.AddSubIfd(value);
        }
        else if (exif.Tag == ExifTag.GPSIFDOffset)
        {
            this.AddSubIfd(value);
        }
        else
        {
            values.Add(exif);
        }
    }

    private void AddInvalidTag(ExifTag tag)
        => (this.invalidTags ??= new List<ExifTag>()).Add(tag);

    private void AddSubIfd(object val)
        => (this.subIfds ??= new List<ulong>()).Add(Convert.ToUInt64(val));

    private void Seek(ulong pos)
        => this.data.Seek((long)pos, SeekOrigin.Begin);

    private bool TryReadSpan(Span<byte> span)
    {
        var length = span.Length;
        if ((this.data.Length - this.data.Position) < length)
        {
            return false;
        }

        var read = this.data.Read(span);
        return read == length;
    }

    protected ulong ReadUInt64() =>
        this.TryReadSpan(this.buf8)
            ? this.ConvertToUInt64(this.buf8)
            : default;

    // Known as Long in Exif Specification.
    protected uint ReadUInt32() =>
        this.TryReadSpan(this.buf4)
            ? this.ConvertToUInt32(this.buf4)
            : default;

    protected ushort ReadUInt16() => this.TryReadSpan(this.buf2)
        ? this.ConvertToShort(this.buf2)
        : default;

    private long ConvertToInt64(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            return default;
        }

        return this.IsBigEndian
            ? BinaryPrimitives.ReadInt64BigEndian(buffer)
            : BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    private ulong ConvertToUInt64(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            return default;
        }

        return this.IsBigEndian
            ? BinaryPrimitives.ReadUInt64BigEndian(buffer)
            : BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    private double ConvertToDouble(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            return default;
        }

        var intValue = this.IsBigEndian
            ? BinaryPrimitives.ReadInt64BigEndian(buffer)
            : BinaryPrimitives.ReadInt64LittleEndian(buffer);

        return Unsafe.As<long, double>(ref intValue);
    }

    private uint ConvertToUInt32(ReadOnlySpan<byte> buffer)
    {
        // Known as Long in Exif Specification.
        if (buffer.Length < 4)
        {
            return default;
        }

        return this.IsBigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(buffer)
            : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private ushort ConvertToShort(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
        {
            return default;
        }

        return this.IsBigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(buffer)
            : BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    private float ConvertToSingle(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4)
        {
            return default;
        }

        var intValue = this.IsBigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(buffer)
            : BinaryPrimitives.ReadInt32LittleEndian(buffer);

        return Unsafe.As<int, float>(ref intValue);
    }

    private Rational ToRational(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            return default;
        }

        var numerator = this.ConvertToUInt32(buffer.Slice(0, 4));
        var denominator = this.ConvertToUInt32(buffer.Slice(4, 4));

        return new Rational(numerator, denominator, false);
    }

    private sbyte ConvertToSignedByte(ReadOnlySpan<byte> buffer) => unchecked((sbyte)buffer[0]);

    private int ConvertToInt32(ReadOnlySpan<byte> buffer) // SignedLong in Exif Specification
    {
        if (buffer.Length < 4)
        {
            return default;
        }

        return this.IsBigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(buffer)
            : BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private SignedRational ToSignedRational(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            return default;
        }

        var numerator = this.ConvertToInt32(buffer.Slice(0, 4));
        var denominator = this.ConvertToInt32(buffer.Slice(4, 4));

        return new SignedRational(numerator, denominator, false);
    }

    private short ConvertToSignedShort(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
        {
            return default;
        }

        return this.IsBigEndian
            ? BinaryPrimitives.ReadInt16BigEndian(buffer)
            : BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }
}