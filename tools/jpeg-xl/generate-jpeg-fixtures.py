#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
# Licensed under the Apache License, Version 2.0.
"""Regenerate original JPEG reconstruction cases with explicit native-tool opt-in."""
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "tests/CodeBrix.Imaging.Tests/SampleFiles/JxlSynthetic/Jpeg"


def run(*args):
    return subprocess.run(args, check=True, capture_output=True)


def segment(marker, payload):
    return bytes((255, marker)) + struct.pack(">H", len(payload) + 2) + payload


def main():
    if os.environ.get("CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS") != "1":
        raise SystemExit("Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to invoke native reference tools.")
    records, files = [], {}
    with tempfile.TemporaryDirectory(prefix="codebrix-jxl-jpeg-") as temporary:
        scratch = Path(temporary)
        cases = [
            ("baseline-444", 19, 13, ["-sample", "1x1,1x1,1x1"]),
            ("baseline-422", 19, 13, ["-sample", "2x1,1x1,1x1"]),
            ("baseline-440", 19, 13, ["-sample", "1x2,1x1,1x1"]),
            ("baseline-420", 19, 13, []),
            ("one-pixel", 1, 1, []),
            ("one-column", 1, 19, []),
            ("one-row", 19, 1, []),
            ("optimized", 263, 17, ["-optimize"]),
            ("restart", 65, 49, ["-restart", "3B"]),
            ("restart-optimized", 65, 49, ["-restart", "1B", "-optimize"]),
            ("progressive-444", 19, 13, ["-progressive", "-sample", "1x1,1x1,1x1"]),
            ("progressive-422", 19, 13, ["-progressive", "-sample", "2x1,1x1,1x1"]),
            ("progressive-440", 19, 13, ["-progressive", "-sample", "1x2,1x1,1x1"]),
            ("progressive-420", 263, 17, ["-progressive"]),
            ("progressive-restart", 65, 49, ["-progressive", "-restart", "3B"]),
            ("gray", 19, 13, ["-grayscale"]),
            ("gray-progressive", 19, 13, ["-grayscale", "-progressive"]),
            ("rgb", 19, 13, ["-rgb"]),
            ("rgb-progressive", 19, 13, ["-rgb", "-progressive"]),
            ("quant16", 19, 13, ["-quality", "1"]),
            ("metadata", 19, 13, []),
            ("padding", 1, 1, []),
            ("fill-bytes", 19, 13, []),
            ("split-icc", 19, 13, []),
        ]
        scan_script = scratch / "scans.txt"
        scan_script.write_text("0; 1; 2;\n")
        cases.append(("sequential-multiscan", 65, 49, ["-scans", str(scan_script), "-restart", "3B"]))
        for name, width, height, flags in cases:
            source, jpeg, jxl, check = (scratch / x for x in ("in.ppm", "in.jpg", "out.jxl", "check.jpg"))
            pixels = bytes((x * (37 + c * 11) + y * (59 - c * 7) + c * 67) % 256
                           for y in range(height) for x in range(width) for c in range(3))
            source.write_bytes(f"P6\n{width} {height}\n255\n".encode() + pixels)
            command = ["cjpeg", "-quality", "83", *flags, "-outfile", str(jpeg), str(source)]
            run(*command)
            original = jpeg.read_bytes()
            if name == "padding":
                original = original[:-3] + bytes((original[-3] & 0xfe,)) + original[-2:]
                jpeg.write_bytes(original)
            if name == "fill-bytes":
                original = original.replace(b"\xff\xdb", b"\xff\xff\xff\xdb", 1)
                jpeg.write_bytes(original)
            if name == "split-icc":
                profile = bytearray((DEST.parent / "Color/lut8.icc").read_bytes())
                # ICC profiles may contain unused padding; this original profile crosses an APP2 boundary.
                profile.extend(bytes(70000 - len(profile)))
                profile[:4] = struct.pack(">I", len(profile))
                pieces = [profile[:65519], profile[65519:]]
                app2 = b"".join(segment(0xe2, b"ICC_PROFILE\0" + bytes((i + 1, len(pieces))) + piece)
                                for i, piece in enumerate(pieces))
                original = original[:2] + app2 + original[2:]
                jpeg.write_bytes(original)
            if name == "metadata":
                # Original minimal TIFF header, XMP packet, generic APP/COM, and post-EOI bytes.
                exif = b"Exif\0\0II\x2a\0\x08\0\0\0\0\0\0\0\0\0"
                xmp = b"http://ns.adobe.com/xap/1.0/\0<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"
                original = original[:2] + segment(0xe1, exif) + segment(0xe1, xmp) + \
                    segment(0xed, b"CodeBrix original test APP13") + segment(0xfe, b"CodeBrix test comment") + \
                    original[2:] + b"CodeBrix post-EOI tail\0\xff"
                jpeg.write_bytes(original)
            run("cjxl", str(jpeg), str(jxl), "--lossless_jpeg=1", "-e", "7", "--num_threads=1")
            run("djxl", str(jxl), str(check), "--num_threads=1")
            if check.read_bytes() != original:
                raise ValueError(f"Native reconstruction differs: {name}")
            encoded = jxl.read_bytes()
            files[f"{name}.jpg"], files[f"{name}.jxl"] = original, encoded
            records.append({"name": name, "width": width, "height": height,
                            "jpegFlags": ["-quality", "83", *["scans.txt" if x == str(scan_script) else x for x in flags]],
                            "inputSha256": hashlib.sha256(encoded).hexdigest(),
                            "referenceSha256": hashlib.sha256(original).hexdigest()})
        metadata = {"license": "Apache-2.0", "copyright": "Copyright (c) 2026 Jeremy Ellis and contributors",
                    "jpegEncoder": run("cjpeg", "-version").stderr.decode().strip(),
                    "jxlEncoder": run("cjxl", "--version").stdout.decode().strip(),
                    "jxlDecoder": run("djxl", "--version").stdout.decode().strip(), "cases": records}
        files["manifest.json"] = (json.dumps(metadata, indent=2) + "\n").encode()
        current = sum(p.stat().st_size for folder in (DEST.parent, DEST.parent.with_name("JxlConformance"))
                      for p in folder.rglob("*") if p.is_file() and not (p.parent == DEST and p.name in files))
        total = current + sum(map(len, files.values()))
        if total > 100_000_000:
            raise SystemExit(f"JPEG XL resource budget exceeded before writing: {total}")
        DEST.mkdir(parents=True, exist_ok=True)
        for name, data in files.items():
            (DEST / name).write_bytes(data)
        print(f"Generated {len(records)} JPEG cases; total JPEG XL resources: {total:,} bytes")


if __name__ == "__main__":
    main()
