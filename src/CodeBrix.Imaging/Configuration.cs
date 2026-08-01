// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Formats.Bmp;
using CodeBrix.Imaging.Formats.Gif;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Pbm;
using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.Formats.Tga;
using CodeBrix.Imaging.Formats.Tiff;
using CodeBrix.Imaging.Formats.Webp;
using CodeBrix.Imaging.IO;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Processing;

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

/// <summary>
/// Provides configuration which allows altering default behaviour or extending the library.
/// </summary>
public sealed class Configuration
{
    /// <summary>
    /// A lazily initialized configuration default instance.
    /// </summary>
    private static readonly Lazy<Configuration> Lazy = new(CreateDefaultInstance);
    private const int DefaultStreamProcessingBufferSize = 8096;
    private int streamProcessingBufferSize = DefaultStreamProcessingBufferSize;
    private int maxDegreeOfParallelism = Environment.ProcessorCount;
    private MemoryAllocator memoryAllocator = MemoryAllocator.Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration" /> class.
    /// </summary>
    public Configuration()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Configuration" /> class.
    /// </summary>
    /// <param name="configurationModules">A collection of configuration modules to register.</param>
    public Configuration(params IConfigurationModule[] configurationModules)
    {
        if (configurationModules != null)
        {
            foreach (var p in configurationModules)
            {
                p.Configure(this);
            }
        }
    }

    /// <summary>
    /// Gets the default <see cref="Configuration"/> instance.
    /// </summary>
    public static Configuration Default { get; } = Lazy.Value;

    /// <summary>
    /// Gets or sets the maximum number of concurrent tasks enabled in ImageSharp algorithms
    /// configured with this <see cref="Configuration"/> instance.
    /// Initialized with <see cref="Environment.ProcessorCount"/> by default.
    /// </summary>
    public int MaxDegreeOfParallelism
    {
        get => this.maxDegreeOfParallelism;
        set
        {
            if (value == 0 || value < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(this.MaxDegreeOfParallelism));
            }

            this.maxDegreeOfParallelism = value;
        }
    }

    /// <summary>
    /// Gets or sets the size of the buffer to use when working with streams.
    /// Initialized with <see cref="DefaultStreamProcessingBufferSize"/> by default.
    /// </summary>
    public int StreamProcessingBufferSize
    {
        get => this.streamProcessingBufferSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(this.StreamProcessingBufferSize));
            }

            this.streamProcessingBufferSize = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to force image buffers to be contiguous whenever possible.
    /// </summary>
    /// <remarks>
    /// Contiguous allocations are not possible, if the image needs a buffer larger than <see cref="int.MaxValue"/>.
    /// </remarks>
    public bool PreferContiguousImageBuffers { get; set; }

    /// <summary>
    /// Gets a set of properties for the Configuration.
    /// </summary>
    /// <remarks>This can be used for storing global settings and defaults to be accessible to processors.</remarks>
    public IDictionary<object, object> Properties { get; } = new ConcurrentDictionary<object, object>();

    /// <summary>
    /// Gets the currently registered <see cref="IImageFormat"/>s.
    /// </summary>
    public IEnumerable<IImageFormat> ImageFormats => this.ImageFormatsManager.ImageFormats;

    /// <summary>
    /// Gets or sets the position in a stream to use for reading when using a seekable stream as an image data source.
    /// </summary>
    public ReadOrigin ReadOrigin { get; set; } = ReadOrigin.Current;

    /// <summary>
    /// Gets or the <see cref="ImageFormatManager"/> that is currently in use.
    /// </summary>
    public ImageFormatManager ImageFormatsManager { get; private set; } = new ImageFormatManager();

    /// <summary>
    /// Gets or sets the <see cref="Memory.MemoryAllocator"/> that is currently in use.
    /// Defaults to <see cref="MemoryAllocator.Default"/>.
    /// <para />
    /// Allocators are expensive, so it is strongly recommended to use only one busy instance per process.
    /// In case you need to customize it, you can ensure this by changing
    /// </summary>
    /// <remarks>
    /// It's possible to reduce allocator footprint by assigning a custom instance created with
    /// <see cref="MemoryAllocator.Create(MemoryAllocatorOptions)"/>, but note that since the default pooling
    /// allocators are expensive, it is strictly recommended to use a single process-wide allocator.
    /// You can ensure this by altering the allocator of <see cref="Default"/>, or by implementing custom application logic that
    /// manages allocator lifetime.
    /// <para />
    /// If an allocator has to be dropped for some reason, <see cref="MemoryAllocator.ReleaseRetainedResources"/>
    /// shall be invoked after disposing all associated <see cref="Image"/> instances.
    /// </remarks>
    public MemoryAllocator MemoryAllocator
    {
        get => this.memoryAllocator;
        set
        {
            Guard.NotNull(value, nameof(this.MemoryAllocator));
            this.memoryAllocator = value;
        }
    }

    /// <summary>
    /// Gets the maximum header size of all the formats.
    /// </summary>
    internal int MaxHeaderSize => this.ImageFormatsManager.MaxHeaderSize;

    /// <summary>
    /// Gets or sets the filesystem helper for accessing the local file system.
    /// </summary>
    internal IFileSystem FileSystem { get; set; } = new LocalFileSystem();

    /// <summary>
    /// Gets or sets the working buffer size hint for image processors.
    /// The default value is 1MB.
    /// </summary>
    /// <remarks>
    /// Currently only used by Resize. If the working buffer is expected to be discontiguous,
    /// min(WorkingBufferSizeHintInBytes, BufferCapacityInBytes) should be used.
    /// </remarks>
    internal int WorkingBufferSizeHintInBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the image operations provider factory.
    /// </summary>
    internal IImageProcessingContextFactory ImageOperationsProvider { get; set; } = new DefaultImageOperationsProviderFactory();

    /// <summary>
    /// Registers a new format provider.
    /// </summary>
    /// <param name="configuration">The configuration provider to call configure on.</param>
    public void Configure(IConfigurationModule configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));
        configuration.Configure(this);
    }

    /// <summary>
    /// Creates a copy of this <see cref="Configuration"/> whose <see cref="MemoryAllocator"/>
    /// refuses to allocate more than <paramref name="allocationLimitMegabytes"/> for a single
    /// image, so that a malicious or corrupt file cannot exhaust process memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decoders trust the dimensions declared in an image header before any pixel data has
    /// been read, so a small file can ask for an enormous buffer - the "decompression bomb"
    /// pattern. The default limit is the platform default (1 GB on 32-bit processes, 4 GB on
    /// 64-bit), which is generous enough that a hostile file can still cause real memory
    /// pressure. Applications that decode untrusted images - anything user-uploaded, or
    /// embedded in a user-supplied document - should decode through a configuration created
    /// here instead of <see cref="Default"/>:
    /// </para>
    /// <code>
    /// var safe = Configuration.Default.CreateSandboxed(256);
    /// using var image = Image.Load(safe, untrustedBytes);
    /// </code>
    /// <para>
    /// Exceeding the limit throws <see cref="InvalidImageContentException"/> from the
    /// <c>Image.Load</c> call, so it is caught by the same handler as any other malformed
    /// image. The limit applies per image, not per process; decoding several images
    /// concurrently can still total more than the limit.
    /// </para>
    /// </remarks>
    /// <param name="allocationLimitMegabytes">
    /// The maximum size, in megabytes, of the (discontiguous) pixel buffer a single decode may
    /// allocate. Must be greater than zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="allocationLimitMegabytes"/> is less than or equal to zero.
    /// </exception>
    /// <returns>A new configuration instance with the allocation limit applied.</returns>
    public Configuration CreateSandboxed(int allocationLimitMegabytes)
    {
        Guard.MustBeGreaterThan(allocationLimitMegabytes, 0, nameof(allocationLimitMegabytes));

        Configuration clone = this.Clone();
        clone.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            AllocationLimitMegabytes = allocationLimitMegabytes
        });

        return clone;
    }

    /// <summary>
    /// Creates a shallow copy of the <see cref="Configuration"/>.
    /// </summary>
    /// <returns>A new configuration instance.</returns>
    public Configuration Clone() => new()
    {
        MaxDegreeOfParallelism = this.MaxDegreeOfParallelism,
        StreamProcessingBufferSize = this.StreamProcessingBufferSize,
        ImageFormatsManager = this.ImageFormatsManager,
        memoryAllocator = this.memoryAllocator,
        ImageOperationsProvider = this.ImageOperationsProvider,
        ReadOrigin = this.ReadOrigin,
        FileSystem = this.FileSystem,
        WorkingBufferSizeHintInBytes = this.WorkingBufferSizeHintInBytes,
    };

    /// <summary>
    /// Creates the default instance with the following <see cref="IConfigurationModule"/>s preregistered:
    /// <see cref="PngConfigurationModule"/>
    /// <see cref="JpegConfigurationModule"/>
    /// <see cref="GifConfigurationModule"/>
    /// <see cref="BmpConfigurationModule"/>.
    /// <see cref="PbmConfigurationModule"/>.
    /// <see cref="TgaConfigurationModule"/>.
    /// <see cref="TiffConfigurationModule"/>.
    /// <see cref="WebpConfigurationModule"/>.
    /// </summary>
    /// <returns>The default configuration of <see cref="Configuration"/>.</returns>
    internal static Configuration CreateDefaultInstance() => new(
        new PngConfigurationModule(),
        new JpegConfigurationModule(),
        new GifConfigurationModule(),
        new BmpConfigurationModule(),
        new PbmConfigurationModule(),
        new TgaConfigurationModule(),
        new TiffConfigurationModule(),
        new WebpConfigurationModule());
}