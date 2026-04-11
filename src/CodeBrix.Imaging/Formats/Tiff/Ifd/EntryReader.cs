// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using CodeBrix.Imaging.Formats.Tiff.Constants;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata.Profiles.Exif;

namespace CodeBrix.Imaging.Formats.Tiff; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff;

internal class EntryReader : BaseExifReader
{
    public EntryReader(Stream stream, ByteOrder byteOrder, MemoryAllocator allocator)
        : base(stream, allocator) =>
        this.IsBigEndian = byteOrder == ByteOrder.BigEndian;

    public List<IExifValue> Values { get; } = new();

    public ulong NextIfdOffset { get; private set; }

    public void ReadTags(bool isBigTiff, ulong ifdOffset)
    {
        if (!isBigTiff)
        {
            this.ReadValues(this.Values, (uint)ifdOffset);
            this.NextIfdOffset = this.ReadUInt32();

            this.ReadSubIfd(this.Values);
        }
        else
        {
            this.ReadValues64(this.Values, ifdOffset);
            this.NextIfdOffset = this.ReadUInt64();

            //// this.ReadSubIfd64(this.Values);
        }
    }

    public void ReadBigValues() => this.ReadBigValues(this.Values);
}

internal class HeaderReader : BaseExifReader
{
    public HeaderReader(Stream stream, ByteOrder byteOrder)
        : base(stream, null) =>
        this.IsBigEndian = byteOrder == ByteOrder.BigEndian;

    public bool IsBigTiff { get; private set; }

    public ulong FirstIfdOffset { get; private set; }

    public void ReadFileHeader()
    {
        var magic = this.ReadUInt16();
        if (magic == TiffConstants.HeaderMagicNumber)
        {
            this.IsBigTiff = false;
            this.FirstIfdOffset = this.ReadUInt32();
            return;
        }
        else if (magic == TiffConstants.BigTiffHeaderMagicNumber)
        {
            this.IsBigTiff = true;

            var bytesize = this.ReadUInt16();
            var reserve = this.ReadUInt16();
            if (bytesize == TiffConstants.BigTiffBytesize && reserve == 0)
            {
                this.FirstIfdOffset = this.ReadUInt64();
                return;
            }
        }

        TiffThrowHelper.ThrowInvalidHeader();
    }
}