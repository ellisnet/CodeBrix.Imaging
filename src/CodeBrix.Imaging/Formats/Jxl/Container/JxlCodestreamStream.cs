// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Container field interpretation adapted from jxl-oxide
// crates/jxl-bitstream/src/container/box_header.rs and container/parse.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// The forward-only Stream adapter is specific to CodeBrix.Imaging.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace CodeBrix.Imaging.Formats.Jxl.Container;

internal sealed class JxlCodestreamStream : Stream
{
    private readonly Stream source;
    private readonly CancellationToken cancellationToken;
    private readonly long maxMetadataBytes;
    private readonly bool ignoreMetadata;
    private long metadataBytes;
    private ulong remaining;
    private uint nextPart;
    private int rawPrefix;
    private bool unknownLength;
    private bool isCodestream;
    private bool sawCodestream;
    private bool lastCodestream;
    private bool sawFileType;
    private bool finished;

    internal JxlCodestreamStream(Stream source, long maxMetadataBytes, bool ignoreMetadata, CancellationToken cancellationToken)
    {
        this.source = source;
        this.maxMetadataBytes = maxMetadataBytes;
        this.ignoreMetadata = ignoreMetadata;
        this.cancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        Span<byte> signature = stackalloc byte[12];
        source.ReadExactly(signature[..2]);
        if (signature[0] == 255 && signature[1] == 10) { this.rawPrefix = 2; return; }
        source.ReadExactly(signature[2..]);
        if (!signature.SequenceEqual(JxlConstants.ContainerSignature)) { JxlThrowHelper.Invalid("container signature mismatch."); }
        this.HasContainer = true;
    }

    internal bool HasContainer { get; }
    internal bool HasJpegReconstruction { get; private set; }
    internal int Level { get; private set; } = 5;
    internal List<(string Type, byte[] Data)> Boxes { get; } = [];

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty || this.finished) { return 0; }
        if (!this.HasContainer)
        {
            int prefix = 0;
            while (this.rawPrefix > 0 && prefix < buffer.Length)
            {
                buffer[prefix++] = this.rawPrefix-- == 2 ? (byte)255 : (byte)10;
            }

            return prefix != 0 ? prefix : this.source.Read(buffer);
        }

        while (!this.finished)
        {
            if (this.isCodestream && (this.unknownLength || this.remaining != 0))
            {
                int take = this.unknownLength ? buffer.Length : (int)Math.Min((ulong)buffer.Length, this.remaining);
                int read = this.source.Read(buffer[..take]);
                if (read == 0)
                {
                    if (!this.unknownLength) { JxlThrowHelper.Invalid("truncated codestream box."); }
                    this.Finish();
                    return 0;
                }

                if (!this.unknownLength) { this.remaining -= (uint)read; }
                return read;
            }

            this.AdvanceBox();
        }

        return 0;
    }

    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return this.Read(value) == 0 ? -1 : value[0];
    }

    internal void Drain()
    {
        Span<byte> scratch = stackalloc byte[4096];
        while (this.Read(scratch) != 0) { }
    }

    private void AdvanceBox()
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        Span<byte> header = stackalloc byte[16];
        int first = this.source.ReadByte();
        if (first < 0) { this.Finish(); return; }
        header[0] = (byte)first;
        this.source.ReadExactly(header.Slice(1, 7));
        ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
        int headerSize = 8;
        if (size == 1)
        {
            this.source.ReadExactly(header[8..]);
            size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            headerSize = 16;
            if (size < 16) { JxlThrowHelper.Invalid("invalid extended box length."); }
        }
        else if (size != 0 && size < 8) { JxlThrowHelper.Invalid("invalid box length."); }

        this.unknownLength = size == 0;
        this.remaining = this.unknownLength ? 0 : size - (uint)headerSize;
        ReadOnlySpan<byte> type = header.Slice(4, 4);
        this.isCodestream = type.SequenceEqual("jxlc"u8) || type.SequenceEqual("jxlp"u8);

        if (!this.sawFileType && !type.SequenceEqual("ftyp"u8)) { JxlThrowHelper.Invalid("missing file type box."); }
        if (type.SequenceEqual("ftyp"u8))
        {
            if (this.sawFileType || this.unknownLength || this.remaining < 12 || (this.remaining & 3) != 0)
            {
                JxlThrowHelper.Invalid("invalid file type box.");
            }

            Span<byte> brand = stackalloc byte[4];
            this.source.ReadExactly(brand);
            if (!brand.SequenceEqual("jxl "u8)) { JxlThrowHelper.Invalid("invalid JPEG XL major brand."); }
            this.source.ReadExactly(brand);
            if (BinaryPrimitives.ReadUInt32BigEndian(brand) != 0) { JxlThrowHelper.Invalid("invalid file type minor version."); }
            this.remaining -= 8;
            bool compatible = false;
            while (this.remaining != 0)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                this.source.ReadExactly(brand);
                compatible |= brand.SequenceEqual("jxl "u8);
                this.remaining -= 4;
            }

            if (!compatible) { JxlThrowHelper.Invalid("missing JPEG XL compatible brand."); }
            this.sawFileType = true;
            return;
        }

        if (this.isCodestream)
        {
            if (this.lastCodestream) { JxlThrowHelper.Invalid("codestream box after the final codestream part."); }
            if (type.SequenceEqual("jxlc"u8))
            {
                if (this.sawCodestream) { JxlThrowHelper.Invalid("mixed codestream box types."); }
                this.lastCodestream = true;
            }
            else
            {
                if (!this.unknownLength && this.remaining < 4) { JxlThrowHelper.Invalid("partial codestream index is missing."); }
                this.source.ReadExactly(header[..4]);
                uint index = BinaryPrimitives.ReadUInt32BigEndian(header);
                this.lastCodestream = (index & 0x80000000) != 0;
                if ((index & 0x7fffffff) != this.nextPart++) { JxlThrowHelper.Invalid("partial codestream index is out of sequence."); }
                if (!this.unknownLength) { this.remaining -= 4; }
            }

            this.sawCodestream = true;
            return;
        }

        if (type.SequenceEqual("jxll"u8))
        {
            if (this.sawCodestream || this.unknownLength || this.remaining != 1) { JxlThrowHelper.Invalid("invalid level box."); }
            this.Level = this.source.ReadByte();
            if (this.Level is not (5 or 10)) { JxlThrowHelper.Invalid("invalid codestream level."); }
            this.remaining = 0;
            return;
        }

        bool compressed = type.SequenceEqual("brob"u8);
        if (compressed)
        {
            if (!this.unknownLength && this.remaining < 4) { JxlThrowHelper.Invalid("compressed metadata box has no type."); }
            this.source.ReadExactly(header[..4]);
            if (!this.unknownLength) { this.remaining -= 4; }
            type = header[..4];
            if (type[..3].SequenceEqual("jxl"u8) || type.SequenceEqual("brob"u8) || type.SequenceEqual("jbrd"u8))
            { JxlThrowHelper.Invalid("box type cannot be Brotli-compressed."); }
        }
        if (type.SequenceEqual("jbrd"u8)) { this.HasJpegReconstruction = true; }
        bool retain = type.SequenceEqual("jbrd"u8) || (!this.ignoreMetadata &&
            (type.SequenceEqual("Exif"u8) || type.SequenceEqual("xml "u8) || type.SequenceEqual("jumb"u8)));
        if (retain)
        {
            string name = System.Text.Encoding.ASCII.GetString(type);
            using MemoryStream data = new();
            Span<byte> chunk = stackalloc byte[4096];
            do
            {
                int take = this.unknownLength ? chunk.Length : (int)Math.Min((ulong)chunk.Length, this.remaining);
                if (take == 0) { break; }
                int read = this.source.Read(chunk[..take]);
                if (read == 0)
                {
                    if (!this.unknownLength) { JxlThrowHelper.Invalid("truncated metadata box."); }
                    break;
                }

                if (read > this.maxMetadataBytes - this.metadataBytes) { JxlThrowHelper.Invalid("metadata exceeds MaxMetadataBytes."); }
                this.metadataBytes += read;
                data.Write(chunk[..read]);
                if (!this.unknownLength) { this.remaining -= (uint)read; }
            }
            while (this.CheckCancellation());
            byte[] payload = data.ToArray();
            if (compressed)
            {
                using BrotliDecoder decoder = new();
                using MemoryStream expanded = new();
                int consumed = 0;
                while (true)
                {
                    this.cancellationToken.ThrowIfCancellationRequested();
                    System.Buffers.OperationStatus status = decoder.Decompress(payload.AsSpan(consumed), chunk, out int read, out int written);
                    consumed += read;
                    if (written > this.maxMetadataBytes - this.metadataBytes) { JxlThrowHelper.Invalid("expanded metadata exceeds MaxMetadataBytes."); }
                    this.metadataBytes += written;
                    expanded.Write(chunk[..written]);
                    if (status == System.Buffers.OperationStatus.Done)
                    {
                        if (consumed != payload.Length) { JxlThrowHelper.Invalid("trailing data in compressed metadata."); }
                        break;
                    }
                    if (status != System.Buffers.OperationStatus.DestinationTooSmall) { JxlThrowHelper.Invalid("invalid compressed metadata."); }
                }
                payload = expanded.ToArray();
            }
            this.Boxes.Add((name, payload));
        }
        else { this.SkipPayload(); }
        if (this.unknownLength) { this.Finish(); }
    }

    private bool CheckCancellation()
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private void SkipPayload()
    {
        Span<byte> scratch = stackalloc byte[4096];
        while (this.unknownLength || this.remaining != 0)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            int take = this.unknownLength ? scratch.Length : (int)Math.Min(this.remaining, (ulong)scratch.Length);
            int read = this.source.Read(scratch[..take]);
            if (read == 0)
            {
                if (!this.unknownLength) { JxlThrowHelper.Invalid("truncated box."); }
                break;
            }

            if (!this.unknownLength) { this.remaining -= (uint)read; }
        }
    }

    private void Finish()
    {
        if (!this.sawCodestream || !this.lastCodestream) { JxlThrowHelper.Invalid("incomplete container codestream."); }
        this.finished = true;
    }

    // This adapter never owns the caller's stream.
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
