using System;
using System.IO;
using System.Reflection;

namespace CodeBrix.Imaging.Tests.Helpers;

/// <summary>
/// Helper class for loading test images from embedded resources.
/// </summary>
internal static class ImageTestHelper
{
    private static readonly Assembly ResourceAssembly = typeof(ImageTestHelper).Assembly;
    private const string ResourcePrefix = "CodeBrix.Imaging.Tests.SampleFiles.";

    /// <summary>
    /// Loads an image from an embedded resource.
    /// </summary>
    /// <param name="resourceName">
    /// The name of the embedded resource file (e.g., "test-image-01.png").
    /// The resource should be located in the SampleFiles folder.
    /// </param>
    /// <returns>An <see cref="Image"/> instance loaded from the embedded resource.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="resourceName"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the embedded resource is not found.</exception>
    public static Image LoadImage(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentNullException(nameof(resourceName));

        var fullResourceName = ResourcePrefix + resourceName;
        using var stream = ResourceAssembly.GetManifestResourceStream(fullResourceName);
        
        if (stream == null)
            throw new InvalidOperationException(
                $"Embedded resource '{fullResourceName}' not found. " +
                $"Available resources: {string.Join(", ", ResourceAssembly.GetManifestResourceNames())}");

        return Image.Load(stream);
    }

    /// <summary>
    /// Gets a stream for an embedded image resource.
    /// </summary>
    /// <param name="resourceName">
    /// The name of the embedded resource file (e.g., "test-image-01.png").
    /// The resource should be located in the SampleFiles folder.
    /// </param>
    /// <returns>A <see cref="Stream"/> for the embedded resource. The caller is responsible for disposing the stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="resourceName"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the embedded resource is not found.</exception>
    public static Stream GetImageStream(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentNullException(nameof(resourceName));

        var fullResourceName = ResourcePrefix + resourceName;
        var stream = ResourceAssembly.GetManifestResourceStream(fullResourceName);
        
        if (stream == null)
            throw new InvalidOperationException(
                $"Embedded resource '{fullResourceName}' not found. " +
                $"Available resources: {string.Join(", ", ResourceAssembly.GetManifestResourceNames())}");

        return stream;
    }
}
