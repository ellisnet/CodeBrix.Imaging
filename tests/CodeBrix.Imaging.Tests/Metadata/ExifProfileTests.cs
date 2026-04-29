using CodeBrix.Imaging.Metadata.Profiles.Exif;
using System;
using System.Linq;
using Xunit;

namespace CodeBrix.Imaging.Tests.Metadata;

/// <summary>
/// Baseline tests for the public <see cref="ExifProfile"/> API.
///
/// These tests exist primarily to detect regressions caused by tightening of
/// internal EXIF parsing limits (e.g. <c>ExifReader.ReadBigValues</c> and
/// <c>ExifReader.ReadValue64</c>). They exercise both the inline value path
/// (values that fit in 4 bytes) and the "big values" path (values that are
/// stored at an offset, &gt; 4 bytes) so that any consumer-visible behavior
/// change in EXIF parsing is caught by the test suite.
/// </summary>
public class ExifProfileTests
{
    private readonly ITestOutputHelper _output;

    public ExifProfileTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void Constructor_Default_CreatesEmptyProfile()
    {
        var profile = new ExifProfile();

        Assert.NotNull(profile);
        Assert.Equal(ExifParts.All, profile.Parts);
        Assert.NotNull(profile.Values);
        Assert.Empty(profile.Values);
        Assert.NotNull(profile.InvalidTags);
        Assert.Empty(profile.InvalidTags);
    }

    [Fact]
    public void Constructor_WithNullData_CreatesEmptyProfile()
    {
        var profile = new ExifProfile((byte[])null);

        Assert.NotNull(profile.Values);
        Assert.Empty(profile.Values);
        Assert.Empty(profile.InvalidTags);
    }

    [Fact]
    public void Constructor_WithEmptyData_DoesNotThrow()
    {
        var profile = new ExifProfile(Array.Empty<byte>());

        // Should not throw on access.
        var values = profile.Values;
        Assert.NotNull(values);
    }

    [Fact]
    public void Constructor_WithTruncatedData_DoesNotThrow()
    {
        // Random truncated bytes that look vaguely like EXIF but are nonsense.
        var bogus = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 };
        var profile = new ExifProfile(bogus);

        // Should not throw - reader needs to be tolerant of malformed input.
        var values = profile.Values;
        Assert.NotNull(values);
    }

    [Fact]
    public void SetValue_WithInlineUInt16_RoundTripsThroughBytes()
    {
        // ExifTag.Orientation is a ushort - fits inline (<=4 bytes).
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)6);

        var data = profile.ToByteArray();
        Assert.NotNull(data);
        Assert.NotEmpty(data);

        var roundTripped = new ExifProfile(data);
        var value = roundTripped.GetValue(ExifTag.Orientation);

        Assert.NotNull(value);
        Assert.Equal((ushort)6, value.Value);
    }

    [Fact]
    public void SetValue_WithShortAsciiString_RoundTripsThroughBytes()
    {
        // 3 chars + null terminator = 4 bytes - fits inline.
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.ImageDescription, "abc");

        var data = profile.ToByteArray();
        var roundTripped = new ExifProfile(data);

        var value = roundTripped.GetValue(ExifTag.ImageDescription);
        Assert.NotNull(value);
        Assert.Equal("abc", value.Value);
    }

    [Fact]
    public void SetValue_WithLongAsciiString_RoundTripsThroughBigValuesPath()
    {
        // A long string forces the >4-byte "big values" path in ExifReader.
        // This is the path most affected by ReadBigValues hardening.
        var longText = new string('x', 256);

        var profile = new ExifProfile();
        profile.SetValue(ExifTag.ImageDescription, longText);
        profile.SetValue(ExifTag.Software, "CodeBrix.Imaging.Tests v1.0");

        var data = profile.ToByteArray();
        Assert.NotNull(data);
        Assert.True(data.Length > longText.Length, "Encoded EXIF should contain the big-value payload.");

        var roundTripped = new ExifProfile(data);

        var description = roundTripped.GetValue(ExifTag.ImageDescription);
        var software = roundTripped.GetValue(ExifTag.Software);

        Assert.NotNull(description);
        Assert.Equal(longText, description.Value);

        Assert.NotNull(software);
        Assert.Equal("CodeBrix.Imaging.Tests v1.0", software.Value);

        Assert.Empty(roundTripped.InvalidTags);
    }

    [Fact]
    public void SetValue_WithRational_RoundTripsThroughBigValuesPath()
    {
        // A Rational is 8 bytes - always exceeds the 4-byte inline limit.
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.XResolution, new Rational(300, 1));
        profile.SetValue(ExifTag.YResolution, new Rational(150, 1));

        var data = profile.ToByteArray();
        var roundTripped = new ExifProfile(data);

        var x = roundTripped.GetValue(ExifTag.XResolution);
        var y = roundTripped.GetValue(ExifTag.YResolution);

        Assert.NotNull(x);
        Assert.NotNull(y);
        Assert.Equal(new Rational(300, 1), x.Value);
        Assert.Equal(new Rational(150, 1), y.Value);
    }

    [Fact]
    public void SetValue_WithUInt16Array_RoundTripsThroughBigValuesPath()
    {
        // Array values force the multi-component "big values" path.
        var samples = new ushort[] { 8, 8, 8, 8 }; // 8 bytes total

        var profile = new ExifProfile();
        profile.SetValue(ExifTag.BitsPerSample, samples);

        var data = profile.ToByteArray();
        var roundTripped = new ExifProfile(data);

        var value = roundTripped.GetValue(ExifTag.BitsPerSample);
        Assert.NotNull(value);
        Assert.Equal(samples, value.Value);
    }

    [Fact]
    public void SetValue_OverwritesExistingValue()
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)1);
        profile.SetValue(ExifTag.Orientation, (ushort)8);

        var value = profile.GetValue(ExifTag.Orientation);
        Assert.NotNull(value);
        Assert.Equal((ushort)8, value.Value);
        Assert.Single(profile.Values, v => v.Tag == ExifTag.Orientation);
    }

    [Fact]
    public void RemoveValue_RemovesPreviouslySetValue()
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)3);
        Assert.NotNull(profile.GetValue(ExifTag.Orientation));

        var removed = profile.RemoveValue(ExifTag.Orientation);

        Assert.True(removed);
        Assert.Null(profile.GetValue(ExifTag.Orientation));
    }

    [Fact]
    public void ToByteArray_WithNoValues_ReturnsEmptyArray()
    {
        var profile = new ExifProfile();

        // Force initialization of the empty values list.
        _ = profile.Values;

        var data = profile.ToByteArray();
        Assert.NotNull(data);
        Assert.Empty(data);
    }

    [Fact]
    public void DeepClone_PreservesAllValues()
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)5);
        profile.SetValue(ExifTag.ImageDescription, "deep clone test value with extra padding to force big-values");
        profile.SetValue(ExifTag.XResolution, new Rational(72, 1));

        var clone = profile.DeepClone();

        Assert.NotSame(profile, clone);
        Assert.Equal((ushort)5, clone.GetValue(ExifTag.Orientation).Value);
        Assert.Equal(
            "deep clone test value with extra padding to force big-values",
            clone.GetValue(ExifTag.ImageDescription).Value);
        Assert.Equal(new Rational(72, 1), clone.GetValue(ExifTag.XResolution).Value);

        // Mutating the clone should not affect the original.
        clone.SetValue(ExifTag.Orientation, (ushort)1);
        Assert.Equal((ushort)5, profile.GetValue(ExifTag.Orientation).Value);
    }

    [Fact]
    public void DeepClone_OfRawDataBackedProfile_PreservesParsedValues()
    {
        var source = new ExifProfile();
        source.SetValue(ExifTag.Software, "RoundTrip");
        source.SetValue(ExifTag.XResolution, new Rational(96, 1));
        var data = source.ToByteArray();

        var fromData = new ExifProfile(data);
        var clone = fromData.DeepClone();

        Assert.Equal("RoundTrip", clone.GetValue(ExifTag.Software).Value);
        Assert.Equal(new Rational(96, 1), clone.GetValue(ExifTag.XResolution).Value);
    }

    [Fact]
    public void RoundTrip_PreservesMultipleHeterogeneousValues()
    {
        // Mixed inline + big-values payload exercises both reader paths in a
        // single EXIF blob, which is the most realistic real-world scenario.
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)1);
        profile.SetValue(ExifTag.Make, "TestMake");
        profile.SetValue(ExifTag.Model, "TestModel-12345");
        profile.SetValue(ExifTag.Software, "CodeBrix.Imaging");
        profile.SetValue(ExifTag.XResolution, new Rational(300, 1));
        profile.SetValue(ExifTag.YResolution, new Rational(300, 1));
        profile.SetValue(ExifTag.BitsPerSample, new ushort[] { 8, 8, 8 });

        var data = profile.ToByteArray();
        var roundTripped = new ExifProfile(data);

        Assert.Equal((ushort)1, roundTripped.GetValue(ExifTag.Orientation).Value);
        Assert.Equal("TestMake", roundTripped.GetValue(ExifTag.Make).Value);
        Assert.Equal("TestModel-12345", roundTripped.GetValue(ExifTag.Model).Value);
        Assert.Equal("CodeBrix.Imaging", roundTripped.GetValue(ExifTag.Software).Value);
        Assert.Equal(new Rational(300, 1), roundTripped.GetValue(ExifTag.XResolution).Value);
        Assert.Equal(new Rational(300, 1), roundTripped.GetValue(ExifTag.YResolution).Value);
        Assert.Equal(new ushort[] { 8, 8, 8 }, roundTripped.GetValue(ExifTag.BitsPerSample).Value);

        Assert.Empty(roundTripped.InvalidTags);

        _output.WriteLine($"Round-tripped EXIF size: {data.Length} bytes; values: {roundTripped.Values.Count}");
    }

    [Fact]
    public void Parts_RestrictsWrittenValues()
    {
        var profile = new ExifProfile
        {
            Parts = ExifParts.IfdTags
        };
        profile.SetValue(ExifTag.Orientation, (ushort)1);

        // ExifIFDOffset etc. is in IfdTags, but exif-only tags should be filtered out.
        var data = profile.ToByteArray();
        Assert.NotNull(data);

        var roundTripped = new ExifProfile(data);
        var orientation = roundTripped.GetValue(ExifTag.Orientation);
        Assert.NotNull(orientation);
        Assert.Equal((ushort)1, orientation.Value);
    }

    [Fact]
    public void Values_CanBeEnumeratedSafely()
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)1);
        profile.SetValue(ExifTag.Make, "X");

        // Snapshot via ToList to make sure the public Values surface is enumerable
        // and stable, which is what consumers depend on.
        var snapshot = profile.Values.ToList();

        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, v => v.Tag == ExifTag.Orientation);
        Assert.Contains(snapshot, v => v.Tag == ExifTag.Make);
    }
}
