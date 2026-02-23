using System;
using System.IO;
using System.Reflection;
using CodeBrix.Imaging.Fonts;

namespace CodeBrix.Imaging.Tests.Helpers;

internal static class FontTestHelper
{
    private const string ResourcePrefix = "CodeBrix.Imaging.Tests.SampleFiles.fonts.";
    private static readonly Assembly ResourceAssembly = typeof(FontTestHelper).Assembly;

    /// <summary>
    /// Loads a font from an embedded resource and returns the FontFamily.
    /// </summary>
    /// <param name="resourceName">The name of the font file (e.g., "Roboto-Regular.ttf").</param>
    /// <param name="collection">The FontCollection to add the font to. If null, a new collection is created.</param>
    /// <returns>The FontFamily loaded from the embedded resource.</returns>
    public static FontFamily LoadFont(string resourceName, FontCollection collection = null)
    {
        collection ??= new FontCollection();
        var fullResourceName = ResourcePrefix + resourceName;
        using var stream = ResourceAssembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{fullResourceName}' not found. " +
                $"Ensure the file exists in the SampleFiles/fonts folder and is set as an Embedded Resource.");
        }

        return collection.Add(stream);
    }

    /// <summary>
    /// Gets a stream for a font embedded resource.
    /// </summary>
    /// <param name="resourceName">The name of the font file (e.g., "Roboto-Regular.ttf").</param>
    /// <returns>A stream containing the font data. Caller is responsible for disposing.</returns>
    public static Stream GetFontStream(string resourceName)
    {
        var fullResourceName = ResourcePrefix + resourceName;
        var stream = ResourceAssembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{fullResourceName}' not found. " +
                $"Ensure the file exists in the SampleFiles/fonts folder and is set as an Embedded Resource.");
        }

        return stream;
    }
}
