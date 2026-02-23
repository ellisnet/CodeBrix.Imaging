using CodeBrix.Imaging.PixelFormats;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace CodeBrix.Imaging.Tests.PixelFormats;

public class Bgra32Tests
{
    private readonly ITestOutputHelper _output;

    public Bgra32Tests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private void ReportPixels(IList<Bgra32> pixels)
    {
        if (pixels is { Count: > 0 })
        {
            foreach (var pixel in pixels)
            {
                _output.WriteLine($"Bgra32: B={pixel.B}, G={pixel.G}, R={pixel.R}, A={pixel.A} (Packed: 0x{pixel.PackedValue:X8})");
            }
        }
    }

    [Fact]
    public void Constructor_with_rgb_creates_opaque_pixel()
    {
        //Arrange & Act
        var pixel = new Bgra32(255, 128, 64);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(64, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Constructor_with_rgba_creates_pixel()
    {
        //Arrange
        var pixels = new List<Bgra32>();

        //Act
        pixels.Add(new Bgra32(255, 0, 0, 255));
        pixels.Add(new Bgra32(0, 255, 0, 128));
        pixels.Add(new Bgra32(0, 0, 255, 64));
        pixels.Add(new Bgra32(128, 128, 128, 0));

        //Assert
        Assert.Equal(4, pixels.Count);
        Assert.Equal(255, pixels[0].R);
        Assert.Equal(255, pixels[1].G);
        Assert.Equal(255, pixels[2].B);
        Assert.Equal(0, pixels[3].A);

        //Output
        ReportPixels(pixels);
    }

    [Fact]
    public void PackedValue_property_works()
    {
        //Arrange
        var pixel = new Bgra32(255, 128, 64, 192);

        //Act
        var packed = pixel.PackedValue;

        //Assert
        Assert.NotEqual(0u, packed);

        //Output
        _output.WriteLine($"Packed value: 0x{packed:X8}");
    }

    [Fact]
    public void Bgra_property_matches_PackedValue()
    {
        //Arrange
        var pixel = new Bgra32(255, 128, 64, 192);

        //Act & Assert
        Assert.Equal(pixel.PackedValue, pixel.Bgra);

        //Output
        _output.WriteLine($"Bgra: 0x{pixel.Bgra:X8}, PackedValue: 0x{pixel.PackedValue:X8}");
    }

    [Fact]
    public void Pixel_fields_are_mutable()
    {
        //Arrange
        var pixel = new Bgra32(0, 0, 0, 0);

        //Act
        pixel.R = 255;
        pixel.G = 128;
        pixel.B = 64;
        pixel.A = 192;

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(64, pixel.B);
        Assert.Equal(192, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Pixel_equality_works()
    {
        //Arrange
        var pixel1 = new Bgra32(255, 128, 64, 192);
        var pixel2 = new Bgra32(255, 128, 64, 192);
        var pixel3 = new Bgra32(0, 0, 0, 255);

        //Act & Assert
        Assert.True(pixel1.Equals(pixel2));
        Assert.False(pixel1.Equals(pixel3));
        Assert.True(pixel1 == pixel2);
        Assert.True(pixel1 != pixel3);

        //Output
        _output.WriteLine($"pixel1 == pixel2: {pixel1 == pixel2}");
        _output.WriteLine($"pixel1 != pixel3: {pixel1 != pixel3}");
    }

    [Fact]
    public void Equals_with_object_works()
    {
        //Arrange
        var pixel = new Bgra32(100, 150, 200, 255);
        object same = new Bgra32(100, 150, 200, 255);
        object different = new Bgra32(0, 0, 0, 0);
        object notPixel = "not a pixel";

        //Act & Assert
        Assert.True(pixel.Equals(same));
        Assert.False(pixel.Equals(different));
        Assert.False(pixel.Equals(notPixel));
        Assert.False(pixel.Equals(null));

        //Output
        _output.WriteLine($"Equals(same): true, Equals(different): false, Equals(string): false, Equals(null): false");
    }

    [Fact]
    public void GetHashCode_is_consistent()
    {
        //Arrange
        var pixel1 = new Bgra32(255, 128, 64, 192);
        var pixel2 = new Bgra32(255, 128, 64, 192);
        var pixel3 = new Bgra32(0, 0, 0, 255);

        //Act & Assert
        Assert.Equal(pixel1.GetHashCode(), pixel2.GetHashCode());
        Assert.NotEqual(pixel1.GetHashCode(), pixel3.GetHashCode());

        //Output
        _output.WriteLine($"pixel1 hash: {pixel1.GetHashCode()}, pixel2 hash: {pixel2.GetHashCode()}, pixel3 hash: {pixel3.GetHashCode()}");
    }

    [Fact]
    public void ToString_returns_expected_format()
    {
        //Arrange
        var pixel = new Bgra32(255, 128, 64, 192);

        //Act
        var result = pixel.ToString();

        //Assert
        Assert.Equal("Bgra32(64, 128, 255, 192)", result);

        //Output
        _output.WriteLine(result);
    }

    [Fact]
    public void PackedValue_setter_updates_fields()
    {
        //Arrange
        var pixel = new Bgra32(0, 0, 0, 0);
        var reference = new Bgra32(64, 128, 255, 192);
        uint packed = reference.PackedValue;

        //Act
        pixel.PackedValue = packed;

        //Assert
        Assert.Equal(reference.R, pixel.R);
        Assert.Equal(reference.G, pixel.G);
        Assert.Equal(reference.B, pixel.B);
        Assert.Equal(reference.A, pixel.A);

        //Output
        _output.WriteLine($"Set PackedValue=0x{packed:X8} => B={pixel.B}, G={pixel.G}, R={pixel.R}, A={pixel.A}");
    }

    [Fact]
    public void Bgra_setter_updates_fields()
    {
        //Arrange
        var pixel = new Bgra32(0, 0, 0, 0);
        var reference = new Bgra32(64, 128, 255, 192);

        //Act
        pixel.Bgra = reference.Bgra;

        //Assert
        Assert.Equal(reference, pixel);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromVector4_and_ToVector4_roundtrip()
    {
        //Arrange
        var vector = new Vector4(1.0f, 0.5f, 0.25f, 0.75f);
        var pixel = new Bgra32();

        //Act
        pixel.FromVector4(vector);
        var result = pixel.ToVector4();

        //Assert - values are quantized to 8-bit, so allow small tolerance
        Assert.InRange(result.X, 0.99f, 1.01f);
        Assert.InRange(result.Y, 0.49f, 0.51f);
        Assert.InRange(result.Z, 0.24f, 0.26f);
        Assert.InRange(result.W, 0.74f, 0.76f);

        //Output
        _output.WriteLine($"Input:  ({vector.X}, {vector.Y}, {vector.Z}, {vector.W})");
        _output.WriteLine($"Output: ({result.X}, {result.Y}, {result.Z}, {result.W})");
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromScaledVector4_and_ToScaledVector4_match_vector4()
    {
        //Arrange
        var vector = new Vector4(0.8f, 0.6f, 0.4f, 0.2f);
        var pixel1 = new Bgra32();
        var pixel2 = new Bgra32();

        //Act
        pixel1.FromVector4(vector);
        pixel2.FromScaledVector4(vector);

        //Assert - FromScaledVector4 delegates to FromVector4
        Assert.Equal(pixel1, pixel2);
        Assert.Equal(pixel1.ToVector4(), pixel2.ToScaledVector4());

        //Output
        ReportPixels([pixel1, pixel2]);
    }

    [Fact]
    public void FromRgba32_converts_correctly()
    {
        //Arrange
        var source = new Rgba32(100, 150, 200, 250);
        var pixel = new Bgra32();

        //Act
        pixel.FromRgba32(source);

        //Assert
        Assert.Equal(100, pixel.R);
        Assert.Equal(150, pixel.G);
        Assert.Equal(200, pixel.B);
        Assert.Equal(250, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void ToRgba32_converts_correctly()
    {
        //Arrange
        var pixel = new Bgra32(100, 150, 200, 250);
        var dest = new Rgba32();

        //Act
        pixel.ToRgba32(ref dest);

        //Assert
        Assert.Equal(100, dest.R);
        Assert.Equal(150, dest.G);
        Assert.Equal(200, dest.B);
        Assert.Equal(250, dest.A);

        //Output
        _output.WriteLine($"Rgba32: R={dest.R}, G={dest.G}, B={dest.B}, A={dest.A}");
    }

    [Fact]
    public void FromArgb32_converts_correctly()
    {
        //Arrange
        var source = new Argb32(100, 150, 200, 250);
        var pixel = new Bgra32();

        //Act
        pixel.FromArgb32(source);

        //Assert
        Assert.Equal(100, pixel.R);
        Assert.Equal(150, pixel.G);
        Assert.Equal(200, pixel.B);
        Assert.Equal(250, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromAbgr32_converts_correctly()
    {
        //Arrange
        var source = new Abgr32(100, 150, 200, 250);
        var pixel = new Bgra32();

        //Act
        pixel.FromAbgr32(source);

        //Assert
        Assert.Equal(100, pixel.R);
        Assert.Equal(150, pixel.G);
        Assert.Equal(200, pixel.B);
        Assert.Equal(250, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromBgra32_copies_value()
    {
        //Arrange
        var source = new Bgra32(100, 150, 200, 250);
        var pixel = new Bgra32();

        //Act
        pixel.FromBgra32(source);

        //Assert
        Assert.Equal(source, pixel);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromRgb24_converts_correctly()
    {
        //Arrange
        var source = new Rgb24(100, 150, 200);
        var pixel = new Bgra32();

        //Act
        pixel.FromRgb24(source);

        //Assert
        Assert.Equal(100, pixel.R);
        Assert.Equal(150, pixel.G);
        Assert.Equal(200, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromBgr24_converts_correctly()
    {
        //Arrange
        var source = new Bgr24(100, 150, 200);
        var pixel = new Bgra32();

        //Act
        pixel.FromBgr24(source);

        //Assert
        Assert.Equal(100, pixel.R);
        Assert.Equal(150, pixel.G);
        Assert.Equal(200, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromL8_converts_correctly()
    {
        //Arrange
        var source = new L8(128);
        var pixel = new Bgra32();

        //Act
        pixel.FromL8(source);

        //Assert
        Assert.Equal(128, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(128, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromL16_converts_correctly()
    {
        //Arrange
        var source = new L16(65535);
        var pixel = new Bgra32();

        //Act
        pixel.FromL16(source);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.Equal(255, pixel.G);
        Assert.Equal(255, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromLa16_converts_correctly()
    {
        //Arrange
        var source = new La16(128, 64);
        var pixel = new Bgra32();

        //Act
        pixel.FromLa16(source);

        //Assert
        Assert.Equal(128, pixel.R);
        Assert.Equal(128, pixel.G);
        Assert.Equal(128, pixel.B);
        Assert.Equal(64, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromLa32_converts_correctly()
    {
        //Arrange
        var source = new La32(65535, 32768);
        var pixel = new Bgra32();

        //Act
        pixel.FromLa32(source);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.Equal(255, pixel.G);
        Assert.Equal(255, pixel.B);
        Assert.InRange(pixel.A, 127, 128);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromRgb48_converts_correctly()
    {
        //Arrange
        var source = new Rgb48(65535, 32768, 0);
        var pixel = new Bgra32();

        //Act
        pixel.FromRgb48(source);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.Equal(0, pixel.B);
        Assert.Equal(255, pixel.A);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void FromRgba64_converts_correctly()
    {
        //Arrange
        var source = new Rgba64(65535, 32768, 16384, 49152);
        var pixel = new Bgra32();

        //Act
        pixel.FromRgba64(source);

        //Assert
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.InRange(pixel.B, 63, 64);
        Assert.InRange(pixel.A, 191, 192);

        //Output
        ReportPixels([pixel]);
    }

    [Fact]
    public void Implicit_conversion_to_Color_and_back()
    {
        //Arrange
        var original = new Bgra32(100, 150, 200, 250);

        //Act
        Color color = original;
        Bgra32 roundtripped = color;

        //Assert
        Assert.Equal(original, roundtripped);

        //Output
        _output.WriteLine($"Original:     B={original.B}, G={original.G}, R={original.R}, A={original.A}");
        _output.WriteLine($"Roundtripped: B={roundtripped.B}, G={roundtripped.G}, R={roundtripped.R}, A={roundtripped.A}");
    }

    [Fact]
    public void Common_colors_have_expected_values()
    {
        //Arrange
        var colors = new Dictionary<string, Bgra32>
        {
            ["Red"] = new Bgra32(255, 0, 0, 255),
            ["Green"] = new Bgra32(0, 255, 0, 255),
            ["Blue"] = new Bgra32(0, 0, 255, 255),
            ["White"] = new Bgra32(255, 255, 255, 255),
            ["Black"] = new Bgra32(0, 0, 0, 255),
            ["Transparent"] = new Bgra32(0, 0, 0, 0)
        };

        //Act & Assert
        Assert.Equal(255, colors["Red"].R);
        Assert.Equal(0, colors["Red"].G);
        Assert.Equal(255, colors["Green"].G);
        Assert.Equal(255, colors["Blue"].B);
        Assert.Equal(255, colors["White"].R);
        Assert.Equal(0, colors["Black"].R);
        Assert.Equal(0, colors["Transparent"].A);

        //Output
        foreach (var kvp in colors)
        {
            _output.WriteLine($"{kvp.Key}: B={kvp.Value.B}, G={kvp.Value.G}, R={kvp.Value.R}, A={kvp.Value.A}");
        }
    }

    [Fact]
    public void CreatePixelOperations_returns_instance()
    {
        //Arrange
        var pixel = new Bgra32(100, 150, 200, 255);

        //Act
        var operations = pixel.CreatePixelOperations();

        //Assert
        Assert.NotNull(operations);

        //Output
        _output.WriteLine($"PixelOperations type: {operations.GetType().Name}");
    }

    // ========================================================================
    // Binary compatibility with System.Drawing.Imaging.PixelFormat.Format32bppArgb
    // ========================================================================
    //
    // System.Drawing.Imaging.PixelFormat.Format32bppArgb stores each pixel as
    // 4 bytes in memory in the order: Blue (byte 0), Green (byte 1), Red (byte 2),
    // Alpha (byte 3). This is the little-endian representation of the 32-bit ARGB
    // value 0xAARRGGBB.
    //
    // Bgra32 uses [StructLayout(LayoutKind.Sequential)] with fields declared in
    // B, G, R, A order, which should produce the identical memory layout.

    [Fact]
    public void Struct_size_is_4_bytes()
    {
        //Arrange & Act
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        int size = Unsafe.SizeOf<Bgra32>();

        //Assert - Format32bppArgb is 32 bits = 4 bytes per pixel
        Assert.Equal(4, size);

        //Output
        _output.WriteLine($"Bgra32 struct size: {size} bytes (expected 4 for Format32bppArgb)");
    }

    [Fact]
    public void Memory_layout_matches_Format32bppArgb_byte_order()
    {
        //Arrange - Create a pixel with distinct values for each channel
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        var pixel = new Bgra32(r: 200, g: 150, b: 100, a: 250);

        //Act - Read the raw bytes of the struct
        Span<byte> bytes = stackalloc byte[4];
        MemoryMarshal.Write(bytes, in pixel);

        //Assert
        // System.Drawing Format32bppArgb memory layout:
        //   byte[0] = Blue
        //   byte[1] = Green
        //   byte[2] = Red
        //   byte[3] = Alpha
        Assert.Equal(100, bytes[0]); // Blue at byte 0
        Assert.Equal(150, bytes[1]); // Green at byte 1
        Assert.Equal(200, bytes[2]); // Red at byte 2
        Assert.Equal(250, bytes[3]); // Alpha at byte 3

        //Output
        _output.WriteLine($"Pixel: R={pixel.R}, G={pixel.G}, B={pixel.B}, A={pixel.A}");
        _output.WriteLine($"Raw bytes: [{bytes[0]:X2}, {bytes[1]:X2}, {bytes[2]:X2}, {bytes[3]:X2}]");
        _output.WriteLine($"Expected Format32bppArgb layout: [Blue={bytes[0]:X2}, Green={bytes[1]:X2}, Red={bytes[2]:X2}, Alpha={bytes[3]:X2}]");
    }

    [Fact]
    public void Packed_uint_matches_Format32bppArgb_uint_representation()
    {
        //Arrange - Create a known pixel: R=0xFF, G=0x80, B=0x40, A=0xC0
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        var pixel = new Bgra32(r: 0xFF, g: 0x80, b: 0x40, a: 0xC0);

        //Act
        uint packed = pixel.PackedValue;

        //Assert
        // In Format32bppArgb, the uint is stored as little-endian BGRA:
        //   packed = (A << 24) | (R << 16) | (G << 8) | B
        // But since Bgra32 uses Unsafe.As<Bgra32, uint> with sequential layout [B, G, R, A],
        // on little-endian systems: packed = B | (G << 8) | (R << 16) | (A << 24)
        uint expected = 0x40u | (0x80u << 8) | (0xFFu << 16) | (0xC0u << 24);
        Assert.Equal(expected, packed);

        //Output
        _output.WriteLine($"Packed value: 0x{packed:X8}");
        _output.WriteLine($"Expected:     0x{expected:X8}");
        _output.WriteLine($"Breakdown: B=0x{pixel.B:X2} | G=0x{pixel.G:X2}<<8 | R=0x{pixel.R:X2}<<16 | A=0x{pixel.A:X2}<<24");
    }

    [Fact]
    public void Field_offsets_match_Format32bppArgb()
    {
        //Arrange - Create a pixel and get its memory
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        var pixel = new Bgra32(r: 0xAA, g: 0xBB, b: 0xCC, a: 0xDD);
        Span<byte> bytes = stackalloc byte[4];
        MemoryMarshal.Write(bytes, in pixel);

        //Act - Verify each field maps to the expected byte offset
        byte fieldB = pixel.B;
        byte fieldG = pixel.G;
        byte fieldR = pixel.R;
        byte fieldA = pixel.A;

        //Assert - Field values must match the bytes at the expected offsets
        Assert.Equal(fieldB, bytes[0]); // B is at offset 0
        Assert.Equal(fieldG, bytes[1]); // G is at offset 1
        Assert.Equal(fieldR, bytes[2]); // R is at offset 2
        Assert.Equal(fieldA, bytes[3]); // A is at offset 3

        //Output
        _output.WriteLine($"Offset 0 (B): field={fieldB:X2}, memory={bytes[0]:X2}");
        _output.WriteLine($"Offset 1 (G): field={fieldG:X2}, memory={bytes[1]:X2}");
        _output.WriteLine($"Offset 2 (R): field={fieldR:X2}, memory={bytes[2]:X2}");
        _output.WriteLine($"Offset 3 (A): field={fieldA:X2}, memory={bytes[3]:X2}");
    }

    [Fact]
    public void Pixel_array_memory_layout_is_contiguous_and_compatible()
    {
        //Arrange - Create an array simulating a row of pixels as Format32bppArgb
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        var pixels = new Bgra32[]
        {
            new(r: 255, g: 0,   b: 0,   a: 255), // Red
            new(r: 0,   g: 255, b: 0,   a: 255), // Green
            new(r: 0,   g: 0,   b: 255, a: 255), // Blue
        };

        //Act - Cast pixel array to byte span (as System.Drawing.Bitmap.LockBits would produce)
        Span<byte> bytes = MemoryMarshal.AsBytes(pixels.AsSpan());

        //Assert - Each pixel occupies 4 contiguous bytes in BGRA order
        Assert.Equal(12, bytes.Length); // 3 pixels × 4 bytes

        // Red pixel: B=0x00, G=0x00, R=0xFF, A=0xFF
        Assert.Equal(0x00, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);
        Assert.Equal(0xFF, bytes[3]);

        // Green pixel: B=0x00, G=0xFF, R=0x00, A=0xFF
        Assert.Equal(0x00, bytes[4]);
        Assert.Equal(0xFF, bytes[5]);
        Assert.Equal(0x00, bytes[6]);
        Assert.Equal(0xFF, bytes[7]);

        // Blue pixel: B=0xFF, G=0x00, R=0x00, A=0xFF
        Assert.Equal(0xFF, bytes[8]);
        Assert.Equal(0x00, bytes[9]);
        Assert.Equal(0x00, bytes[10]);
        Assert.Equal(0xFF, bytes[11]);

        //Output
        for (int i = 0; i < pixels.Length; i++)
        {
            int offset = i * 4;
            _output.WriteLine($"Pixel {i}: B=0x{bytes[offset]:X2}, G=0x{bytes[offset + 1]:X2}, R=0x{bytes[offset + 2]:X2}, A=0x{bytes[offset + 3]:X2}");
        }
    }

    [Fact]
    public void Packed_value_can_be_constructed_as_Format32bppArgb_int()
    {
        //Arrange - Construct an ARGB integer value as System.Drawing.Color.FromArgb would produce
        // System.Drawing stores ARGB as: (alpha << 24) | (red << 16) | (green << 8) | blue
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        byte r = 200, g = 100, b = 50, a = 255;
        int argbInt = (a << 24) | (r << 16) | (g << 8) | b;

        //Act - Create Bgra32 from those components and compare packed value
        var pixel = new Bgra32(r, g, b, a);

        //Assert
        // Bgra32.PackedValue (via Unsafe.As on sequential B,G,R,A) on little-endian = B | (G<<8) | (R<<16) | (A<<24)
        // Which is the same as the ARGB int when interpreted as uint on little-endian
        Assert.Equal((uint)argbInt, pixel.PackedValue);

        //Output
        _output.WriteLine($"ARGB int: 0x{argbInt:X8}");
        _output.WriteLine($"PackedValue: 0x{pixel.PackedValue:X8}");
        _output.WriteLine($"Match: {(uint)argbInt == pixel.PackedValue}");
    }

    [Fact]
    public void Bgra32_roundtrips_through_byte_manipulation_like_LockBits()
    {
        //Arrange - Simulate reading pixel data as if from Bitmap.LockBits with Format32bppArgb
        Assert.True(BitConverter.IsLittleEndian, "Binary compatibility with Format32bppArgb requires a little-endian platform.");
        byte[] bitmapData = [0x40, 0x80, 0xC0, 0xFF];

        //Act - Interpret the raw bytes as a Bgra32 struct (as one would with LockBits scan0 pointer)
        var pixel = MemoryMarshal.Read<Bgra32>(bitmapData);

        //Assert
        Assert.Equal(0x40, pixel.B);
        Assert.Equal(0x80, pixel.G);
        Assert.Equal(0xC0, pixel.R);
        Assert.Equal(0xFF, pixel.A);

        // And write it back
        Span<byte> roundtrippedBytes = stackalloc byte[4];
        MemoryMarshal.Write(roundtrippedBytes, in pixel);
        Assert.Equal(bitmapData[0], roundtrippedBytes[0]);
        Assert.Equal(bitmapData[1], roundtrippedBytes[1]);
        Assert.Equal(bitmapData[2], roundtrippedBytes[2]);
        Assert.Equal(bitmapData[3], roundtrippedBytes[3]);

        //Output
        _output.WriteLine($"Simulated LockBits data: [0x{bitmapData[0]:X2}, 0x{bitmapData[1]:X2}, 0x{bitmapData[2]:X2}, 0x{bitmapData[3]:X2}]");
        _output.WriteLine($"Interpreted as Bgra32: B={pixel.B}, G={pixel.G}, R={pixel.R}, A={pixel.A}");
        _output.WriteLine($"Roundtripped bytes: [0x{roundtrippedBytes[0]:X2}, 0x{roundtrippedBytes[1]:X2}, 0x{roundtrippedBytes[2]:X2}, 0x{roundtrippedBytes[3]:X2}]");
    }
}
