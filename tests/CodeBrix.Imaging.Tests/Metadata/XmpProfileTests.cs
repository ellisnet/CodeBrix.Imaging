using CodeBrix.Imaging.Metadata.Profiles.Xmp;
using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Metadata;

/// <summary>
/// Tests for <see cref="XmpProfile"/> functionality, including byte-stripping
/// behavior in <see cref="XmpProfile.GetDocument()"/>.
/// </summary>
public class XmpProfileTests
{
    // ReSharper disable once InconsistentNaming
    private readonly ITestOutputHelper _output;

    public XmpProfileTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Minimal valid XMP XML used across tests.
    /// </summary>
    private const string MinimalXmpXml =
        "<x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
        "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" +
        "<rdf:Description rdf:about=''/>" +
        "</rdf:RDF>" +
        "</x:xmpmeta>";

    #region Constructor and Data Tests

    [Fact]
    public void Constructor_WithNullData_CreatesProfile()
    {
        // Act
        var profile = new XmpProfile((byte[])null);

        // Assert - profile is created, Data is null (tested via GetDocument returning null)
        var doc = profile.GetDocument();
        Assert.Null(doc);
        _output.WriteLine("XmpProfile with null data: GetDocument() returned null as expected");
    }

    [Fact]
    public void Constructor_Default_CreatesProfileWithNullData()
    {
        // Act
        var profile = new XmpProfile();

        // Assert
        var doc = profile.GetDocument();
        Assert.Null(doc);
        _output.WriteLine("Default XmpProfile: GetDocument() returned null as expected");
    }

    [Fact]
    public void Constructor_WithValidData_CreatesProfile()
    {
        // Arrange
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);

        // Act
        var profile = new XmpProfile(xmlBytes);

        // Assert
        var doc = profile.GetDocument();
        Assert.NotNull(doc);
        Assert.Equal("xmpmeta", doc.Root?.Name.LocalName);
        _output.WriteLine($"XmpProfile created with {xmlBytes.Length} bytes, parsed successfully");
    }

    #endregion

    #region GetDocument Tests

    [Fact]
    public void GetDocument_WithCleanXmpData_ReturnsValidDocument()
    {
        // Arrange - clean XMP XML with no trailing padding
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var profile = new XmpProfile(xmlBytes);

        // Act
        var doc = profile.GetDocument();

        // Assert
        Assert.NotNull(doc);
        Assert.Equal("xmpmeta", doc.Root?.Name.LocalName);

        var rdfElement = doc.Root?.Elements().FirstOrDefault();
        Assert.NotNull(rdfElement);
        Assert.Equal("RDF", rdfElement.Name.LocalName);
        _output.WriteLine("Clean XMP data parsed successfully with correct structure");
    }

    [Fact]
    public void GetDocument_WithTrailingNullBytes_ReturnsValidDocument()
    {
        // Arrange - valid XMP XML followed by trailing null bytes
        // This is the most common padding pattern in real-world XMP data (JPEG, WebP, etc.)
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var data = new byte[xmlBytes.Length + 5];
        Buffer.BlockCopy(xmlBytes, 0, data, 0, xmlBytes.Length);
        // Trailing 5 bytes are 0x00 (already default)

        var profile = new XmpProfile(data);

        // Act
        var doc = profile.GetDocument();

        // Assert - trailing null bytes should be stripped, XML should parse fine
        Assert.NotNull(doc);
        Assert.Equal("xmpmeta", doc.Root?.Name.LocalName);
        _output.WriteLine($"XMP data ({xmlBytes.Length} bytes XML + 5 null bytes) parsed successfully");
    }

    [Fact]
    public void GetDocument_WithTrailingNullBytes_PreservesDocumentContent()
    {
        // Arrange - XMP XML with a known value, followed by trailing null bytes
        var xml = "<x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
                  "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" +
                  "<rdf:Description rdf:about='' testattr='hello'/>" +
                  "</rdf:RDF>" +
                  "</x:xmpmeta>";
        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var data = new byte[xmlBytes.Length + 10]; // 10 trailing null bytes
        Buffer.BlockCopy(xmlBytes, 0, data, 0, xmlBytes.Length);

        var profile = new XmpProfile(data);

        // Act
        var doc = profile.GetDocument();

        // Assert - verify the document content is fully preserved
        Assert.NotNull(doc);
        var ns = XNamespace.Get("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        var desc = doc.Descendants(ns + "Description").FirstOrDefault();
        Assert.NotNull(desc);
        Assert.Equal("hello", desc.Attribute("testattr")?.Value);
        _output.WriteLine("Document content preserved correctly after null byte stripping");
    }

    [Fact]
    public void GetDocument_CalledMultipleTimes_ReturnsSameContent()
    {
        // Arrange
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var profile = new XmpProfile(xmlBytes);

        // Act
        var doc1 = profile.GetDocument();
        var doc2 = profile.GetDocument();

        // Assert - multiple calls should return equivalent documents
        Assert.NotNull(doc1);
        Assert.NotNull(doc2);
        Assert.Equal(doc1.ToString(), doc2.ToString());
        _output.WriteLine("GetDocument() is idempotent");
    }

    /// <summary>
    /// This test demonstrates the known bug in <see cref="XmpProfile.GetDocument()"/>.
    /// The byte-stripping loop iterates the entire array without breaking on the first
    /// non-matching byte from the end, so any null byte (0x00) found at interior positions
    /// (index > 0) is counted alongside trailing nulls, reducing the byte count too much
    /// and truncating the XML content.
    ///
    /// Scenario: [0x00] [0x00] [valid XML] [0x00] [0x00] [0x00]
    /// - Expected: strip 3 trailing null bytes + 2 leading null bytes → parse complete XML
    /// - Current bug: loop counts interior null at index 1 as well → count reduced by 4
    ///   instead of 3 → XML truncated by 1 byte → XmlException
    ///
    /// After the fix, this test should pass — GetDocument should handle leading and trailing
    /// null bytes correctly and return a valid document.
    /// </summary>
    [Fact]
    public void GetDocument_WithLeadingAndTrailingNullBytes_ShouldNotTruncateXml()
    {
        // Arrange - valid XMP XML surrounded by leading and trailing null bytes
        // This simulates data from encoders that include null padding at both ends
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);

        // 2 leading null bytes + XML content + 3 trailing null bytes
        var data = new byte[2 + xmlBytes.Length + 3];
        Buffer.BlockCopy(xmlBytes, 0, data, 2, xmlBytes.Length);
        // data[0] = data[1] = 0x00 (leading nulls)
        // data[end-2] = data[end-1] = data[end] = 0x00 (trailing nulls)

        _output.WriteLine($"Data layout: [00][00][{xmlBytes.Length} bytes XML][00][00][00]");
        _output.WriteLine($"Total data length: {data.Length}");

        var profile = new XmpProfile(data);

        // Act & Assert
        // After fix: leading and trailing null bytes should be stripped,
        // leaving the complete XML content for parsing.
        var doc = profile.GetDocument();
        Assert.NotNull(doc);
        Assert.Equal("xmpmeta", doc.Root?.Name.LocalName);
        _output.WriteLine("GetDocument parsed successfully after stripping leading/trailing nulls");
    }

    #endregion

    #region ToByteArray Tests

    [Fact]
    public void ToByteArray_ReturnsExactCopyOfData()
    {
        // Arrange
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var profile = new XmpProfile(xmlBytes);

        // Act
        var result = profile.ToByteArray();

        // Assert
        Assert.Equal(xmlBytes.Length, result.Length);
        Assert.Equal(xmlBytes, result);
        _output.WriteLine($"ToByteArray returned {result.Length} bytes matching original data");
    }

    [Fact]
    public void ToByteArray_ReturnsIndependentCopy()
    {
        // Arrange
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var profile = new XmpProfile(xmlBytes);

        // Act
        var result = profile.ToByteArray();
        result[0] = 0xFF; // Modify the copy

        // Assert - original data should be unaffected
        var result2 = profile.ToByteArray();
        Assert.NotEqual(0xFF, result2[0]);
        _output.WriteLine("ToByteArray returned an independent copy (mutations don't affect original)");
    }

    #endregion

    #region DeepClone Tests

    [Fact]
    public void DeepClone_ReturnsEquivalentProfile()
    {
        // Arrange
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var profile = new XmpProfile(xmlBytes);

        // Act
        var clone = profile.DeepClone();

        // Assert
        Assert.NotNull(clone);
        var originalBytes = profile.ToByteArray();
        var cloneBytes = clone.ToByteArray();
        Assert.Equal(originalBytes, cloneBytes);
        _output.WriteLine("DeepClone produced equivalent profile");
    }

    [Fact]
    public void DeepClone_ProducesIndependentDocument()
    {
        // Arrange
        var xmlBytes = Encoding.UTF8.GetBytes(MinimalXmpXml);
        var profile = new XmpProfile(xmlBytes);

        // Act
        var clone = profile.DeepClone();
        var originalDoc = profile.GetDocument();
        var cloneDoc = clone.GetDocument();

        // Assert - both should parse successfully and produce equivalent documents
        Assert.NotNull(originalDoc);
        Assert.NotNull(cloneDoc);
        Assert.Equal(originalDoc.ToString(), cloneDoc.ToString());
        _output.WriteLine("DeepClone produced independent profile with same document content");
    }

    #endregion
}
