// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Handwritten 31-bit precision fixture using the audited JPEG XL field writer.
using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;
if (args.Length != 1) { throw new ArgumentException("Pass the destination integer31.jxl path."); }
using var payload = new MemoryStream();
var p = new JxlBitWriter(payload);
p.WriteBool(true); // Default LF dequantization.
p.WriteBool(false); // No global MA tree.
p.WriteBool(false); // Local Modular tree.
p.WriteBool(true); // Default weighted predictor.
p.WriteBits(2, 0); // No Modular transforms.
p.WriteBool(false); // Tree entropy: no LZ77.
p.WriteBool(true); p.WriteBits(2, 0); // Six tree contexts map to cluster 0.
p.WriteBool(true); p.WriteBits(4, 15); p.WriteBool(false); // Prefix alphabet {0}; direct hybrid integers.
// The implicit five zero tree tokens specify predictor 0, offset 0 and multiplier 1.
p.WriteBool(false); p.WriteBool(true); p.WriteBits(4, 0); // Pixel entropy: prefix, hybrid split 0.
p.WriteBool(true); p.WriteBits(4, 5); p.WriteBits(5, 0); // Alphabet size 33.
p.WriteBits(2, 1); p.WriteBits(2, 0); p.WriteBits(6, 32); // Constant prefix symbol 32.
p.WriteBits(31, 2); p.WriteBits(31, 0x7ffffffe); // Zigzag residuals 0x80000002 and 0xfffffffe.
p.ZeroPadToByte();
using var file = new MemoryStream(); var w = new JxlBitWriter(file);
new JxlImageHeader { Size = new(2, 1), BitDepth = new(31, 0), Modular16BitBuffers = false,
    XybEncoded = false, Color = new JxlColorEncoding { ColorSpace = 1 } }.Write(w);
w.ZeroPadToByte();
w.WriteBool(false); w.WriteBits(2, 0); w.WriteBool(true); // Normal Modular frame.
w.WriteU64(0); w.WriteBool(false); w.WriteBits(2, 0); // No flags, no YCbCr, upsampling 1.
w.WriteBits(2, 1); w.WriteBits(2, 0); w.WriteBool(false); // Group shift 1, one pass, no crop.
w.WriteBits(2, 0); w.WriteBool(true); // Replace and last frame.
JxlName.Write(w, "");
w.WriteBool(false); w.WriteBool(false); w.WriteBits(2, 0); // Disable both restoration filters.
w.WriteU64(0); w.WriteU64(0); // No restoration/frame extensions.
w.WriteBool(false); w.ZeroPadToByte(); // TOC: no permutation.
w.WriteU32((uint)payload.Length, U(10), U(14, 1024), U(22, 17408), U(30, 4211712));
w.ZeroPadToByte(); file.Write(payload.ToArray());
File.WriteAllBytes(Path.GetFullPath(args[0]), file.ToArray());
Console.WriteLine($"Wrote {file.Length} bytes, payload {payload.Length} bytes.");
