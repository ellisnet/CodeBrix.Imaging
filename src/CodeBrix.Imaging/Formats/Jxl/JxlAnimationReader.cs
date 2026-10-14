// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Decodes JPEG XL canvases on a background task into a bounded playback queue.</summary>
/// <remarks>Use one consumer. Dispose every returned frame. Seeking requires exclusive access to the reader;
/// disposal and cancellation may interrupt a pending read. The reader does not wait for frame display durations.</remarks>
public sealed class JxlAnimationReader : IDisposable, IAsyncDisposable, IAsyncEnumerable<JxlAnimationFrame>
{
    private readonly Configuration configuration;
    private readonly Stream original, input;
    private readonly JxlAnimationReaderOptions options;
    private readonly CancellationToken cancellationToken;
    private readonly long initialPosition;
    private readonly object lifecycle = new();
    private readonly TaskCompletionSource<bool> headerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource generationCancellation;
    private Channel<JxlAnimationFrame> channel;
    private Task producer;
    private ImageMetadata metadata;
    private bool disposed;

    private JxlAnimationReader(Configuration configuration, Stream stream, JxlAnimationReaderOptions options, CancellationToken cancellationToken)
    {
        this.configuration = configuration.Clone(); this.original = stream; this.options = options; this.cancellationToken = cancellationToken;
        this.input = !stream.CanSeek && options.Loop ? new JxlReplayStream(stream, options.MaxBufferedInputBytes) : stream;
        this.initialPosition = this.input.CanSeek ? this.input.Position : 0;
        this.Start(TimeSpan.Zero);
    }

    /// <summary>Opens an animation using the default allocator. Only the image header is required before this method returns.</summary>
    /// <param name="stream">The readable input stream.</param>
    /// <param name="options">Playback options, or null for defaults.</param>
    /// <param name="cancellationToken">Cancellation for the reader's entire lifetime.</param>
    /// <returns>The owned streaming reader.</returns>
    public static JxlAnimationReader Open(Stream stream, JxlAnimationReaderOptions options = null, CancellationToken cancellationToken = default) =>
        Open(Configuration.Default, stream, options, cancellationToken);

    /// <summary>Opens an animation using the supplied allocator and parallelism configuration.</summary>
    /// <param name="configuration">The image configuration, copied on open.</param>
    /// <param name="stream">The readable input stream.</param>
    /// <param name="options">Playback options, or null for defaults.</param>
    /// <param name="cancellationToken">Cancellation for the reader's entire lifetime.</param>
    /// <returns>The owned streaming reader.</returns>
    public static JxlAnimationReader Open(Configuration configuration, Stream stream, JxlAnimationReaderOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) { throw new ArgumentException("The JPEG XL input must be readable.", nameof(stream)); }
        options ??= new();
        if (options.DecodeAhead is < 1 or > 64) { throw new ArgumentOutOfRangeException(nameof(options.DecodeAhead)); }
        if (options.PixelFormat is not (JxlPixelFormat.Rgba32 or JxlPixelFormat.Bgra32)) { throw new ArgumentOutOfRangeException(nameof(options.PixelFormat)); }
        if (options.MaxBufferedInputBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(options.MaxBufferedInputBytes)); }
        ArgumentNullException.ThrowIfNull(options.Decoder);
        cancellationToken.ThrowIfCancellationRequested();
        JxlAnimationReaderOptions snapshot = new() { DecodeAhead = options.DecodeAhead, PixelFormat = options.PixelFormat,
            Loop = options.Loop, LeaveOpen = options.LeaveOpen, MaxBufferedInputBytes = options.MaxBufferedInputBytes,
            Decoder = options.Decoder.CopyValidatedOptions() };
        JxlAnimationReader reader = new(configuration, stream, snapshot, cancellationToken);
        try { reader.headerReady.Task.GetAwaiter().GetResult(); return reader; }
        catch { reader.Dispose(); throw; }
    }

    /// <summary>Gets the oriented canvas width.</summary>
    public int Width { get; private set; }
    /// <summary>Gets the oriented canvas height.</summary>
    public int Height { get; private set; }
    /// <summary>Gets image metadata. Boxes following image data and the frame count become available after the first complete pass.</summary>
    public ImageMetadata Metadata => Volatile.Read(ref this.metadata);
    /// <summary>Gets whether the input can be replayed for seeking.</summary>
    public bool CanSeek => this.input.CanSeek;

    /// <summary>Waits for the next canvas, returning false at the end of playback.</summary>
    /// <param name="frame">An owned frame, or null at the end of playback.</param>
    /// <returns>Whether a frame was returned.</returns>
    public bool TryGetNextFrame(out JxlAnimationFrame frame)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ChannelReader<JxlAnimationFrame> reader = this.channel.Reader;
        while (reader.WaitToReadAsync(this.cancellationToken).AsTask().GetAwaiter().GetResult())
        { if (reader.TryRead(out frame)) { return true; } }
        frame = null; return false;
    }

    /// <summary>Asynchronously reads owned canvases until playback ends. Dispose each yielded frame.</summary>
    /// <param name="cancellationToken">Cancellation for this enumeration.</param>
    /// <returns>The stream of owned frames.</returns>
    public async IAsyncEnumerable<JxlAnimationFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(this.cancellationToken, cancellationToken);
        await foreach (JxlAnimationFrame frame in this.channel.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false))
        { yield return frame; }
    }

    /// <inheritdoc/>
    public IAsyncEnumerator<JxlAnimationFrame> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        this.ReadFramesAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    /// <summary>Restarts at the displayed frame containing the requested time within one playback iteration.</summary>
    /// <param name="timestamp">The nonnegative target time; a time beyond the animation reaches end of playback.</param>
    /// <remarks>Rewinds compressed input and decodes preceding dependencies without queuing them. Input must be seekable or replay-cached.</remarks>
    public void SeekToKeyframe(TimeSpan timestamp)
    {
        if (timestamp < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(timestamp)); }
        lock (this.lifecycle)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            if (!this.input.CanSeek) { throw new NotSupportedException("Seeking requires seekable input or an enabled replay cache."); }
            this.generationCancellation.Cancel(); this.producer.GetAwaiter().GetResult();
            Drain(this.channel); this.generationCancellation.Dispose();
            this.input.Position = this.initialPosition;
            this.Start(timestamp);
        }
    }

    private void Start(TimeSpan skipUntil)
    {
        this.generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.cancellationToken);
        this.channel = Channel.CreateBounded<JxlAnimationFrame>(new BoundedChannelOptions(this.options.DecodeAhead)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        Channel<JxlAnimationFrame> destination = this.channel;
        CancellationToken token = this.generationCancellation.Token;
        this.producer = Task.Run(() => this.Produce(destination, skipUntil, token), CancellationToken.None);
    }

    private void Produce(Channel<JxlAnimationFrame> destination, TimeSpan skipUntil, CancellationToken token)
    {
        try
        {
            uint iteration = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                TimeSpan timestamp = TimeSpan.Zero;
                int delivered = 0;
                ImageMetadata completed = new JxlDecoderCore(this.options.Decoder).DecodeAnimation(this.configuration, this.input, this.options.PixelFormat,
                    (header, initialMetadata) =>
                    {
                        this.Width = (int)header.Size.Width; this.Height = (int)header.Size.Height;
                        if (this.options.Decoder.ApplyOrientation && header.Orientation > 4) { (this.Width, this.Height) = (this.Height, this.Width); }
                        if (this.metadata == null) { Volatile.Write(ref this.metadata, initialMetadata); }
                        this.headerReady.TrySetResult(true);
                    }, frame =>
                    {
                        frame.Timestamp = timestamp; frame.LoopIndex = iteration;
                        timestamp = frame.Duration.Ticks > long.MaxValue - timestamp.Ticks ? TimeSpan.MaxValue : timestamp + frame.Duration;
                        if (timestamp <= skipUntil && (frame.Duration != TimeSpan.Zero || frame.Timestamp < skipUntil)) { frame.Dispose(); return; }
                        destination.Writer.WriteAsync(frame, token).AsTask().GetAwaiter().GetResult();
                        delivered++;
                    }, token);
                Volatile.Write(ref this.metadata, completed);
                JxlMetadata info = completed.GetJxlMetadata();
                iteration++;
                if (!this.options.Loop || !info.IsAnimated || delivered == 0 ||
                    (info.LoopCount != 0 && iteration >= info.LoopCount)) { break; }
                skipUntil = TimeSpan.Zero;
                this.input.Position = this.initialPosition;
            }
            destination.Writer.TryComplete();
        }
        catch (Exception error)
        {
            Exception translated = error is EndOfStreamException ? new InvalidImageContentException("Truncated JPEG XL animation.", error)
                : error is InvalidMemoryOperationException ? new InvalidImageContentException("JPEG XL animation exceeds the allocator limits.", error) : error;
            this.headerReady.TrySetException(translated); destination.Writer.TryComplete(translated);
        }
    }

    private static void Drain(Channel<JxlAnimationFrame> channel)
    { while (channel.Reader.TryRead(out JxlAnimationFrame frame)) { frame.Dispose(); } }

    /// <inheritdoc/>
    public void Dispose() => this.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (this.lifecycle)
        {
            if (this.disposed) { return; }
            this.disposed = true; this.generationCancellation.Cancel(); pending = this.producer;
        }
        await pending.ConfigureAwait(false);
        Drain(this.channel); this.generationCancellation.Dispose();
        if (!ReferenceEquals(this.input, this.original)) { this.input.Dispose(); }
        if (!this.options.LeaveOpen) { this.original.Dispose(); }
    }
}
