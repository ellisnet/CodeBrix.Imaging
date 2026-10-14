#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
# Licensed under the Apache License, Version 2.0.
"""Regenerate small, original JPEG XL sampling fixtures with explicit native-tool opt-in."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "tests/CodeBrix.Imaging.Tests/SampleFiles/JxlSynthetic"


def run(*args):
    return subprocess.run(args, check=True, capture_output=True)


def main():
    if os.environ.get("CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS") != "1":
        raise SystemExit("Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to invoke native reference tools.")
    parser = argparse.ArgumentParser()
    parser.add_argument("--vardct", action="store_true")
    parser.add_argument("--noise", action="store_true")
    parser.add_argument("--progressive", action="store_true")
    options = parser.parse_args()
    if options.noise and options.progressive:
        parser.error("Choose one feature family per manifest")
    options.vardct |= options.noise or options.progressive
    family = "noise" if options.noise else "progressive" if options.progressive else "vardct" if options.vardct else "rgb"
    records = []
    with tempfile.TemporaryDirectory(prefix="codebrix-jxl-sampling-") as temporary:
        scratch = Path(temporary)
        for width, height in (((1, 1), (1, 7), (9, 1), (19, 13), (263, 17)) if options.vardct else ((1, 1), (1, 7), (9, 1), (19, 13))):
            pixels = bytes((x * (37 + c * 11) + y * (59 - c * 7) + c * 67) % 256
                           for y in range(height) for x in range(width) for c in range(3))
            source = scratch / "input.ppm"
            source.write_bytes(f"P6\n{width} {height}\n255\n".encode() + pixels)
            for factor in ((1, 2, 4, 8) if options.vardct else (2, 4, 8)):
                name = f"{family}-{width}x{height}-up{factor}"
                encoded, decoded = scratch / f"{name}.jxl", scratch / "reference.pfm"
                run("cjxl", str(source), str(encoded), "-m", "0" if options.vardct else "1", "-d", "1", "-e", "5",
                    f"--resampling={factor}", "--num_threads=1",
                    *(["--photon_noise_iso=3200"] if options.noise else []),
                    *(["--progressive_dc=2", "--progressive_ac"] if options.progressive else []))
                run("djxl", str(encoded), str(decoded), "--num_threads=1")
                with decoded.open("rb") as f:
                    if f.readline().strip() != b"PF":
                        raise ValueError("Expected RGB PFM")
                    out_width, out_height = map(int, f.readline().split())
                    scale = float(f.readline())
                    if abs(scale) != 1.0:
                        raise ValueError("Unexpected PFM scale")
                    payload = f.read()
                    if scale > 0:
                        payload = b"".join(payload[i:i + 4][::-1] for i in range(0, len(payload), 4))
                stride = out_width * 12
                # PFM scanlines are stored bottom-to-top.
                payload = b"".join(payload[y * stride:(y + 1) * stride] for y in range(out_height - 1, -1, -1))
                header = f"{{'descr': '<f4', 'fortran_order': False, 'shape': (1, {out_height}, {out_width}, 3), }}"
                header += " " * ((-len(header) - 11) % 64) + "\n"
                npy = b"\x93NUMPY\x01\x00" + struct.pack("<H", len(header)) + header.encode() + payload
                DEST.mkdir(parents=True, exist_ok=True)
                (DEST / f"{name}.jxl").write_bytes(encoded.read_bytes())
                (DEST / f"{name}.npy").write_bytes(npy)
                records.append({"name": name, "width": width, "height": height, "upsampling": factor,
                                "inputSha256": hashlib.sha256(encoded.read_bytes()).hexdigest(),
                                "referenceSha256": hashlib.sha256(npy).hexdigest()})
    metadata = {"license": "Apache-2.0", "copyright": "Copyright (c) 2026 Jeremy Ellis and contributors",
                "encoder": run("cjxl", "--version").stdout.decode().strip(),
                "decoder": run("djxl", "--version").stdout.decode().strip(), "cases": records}
    (DEST / (f"{family}-manifest.json" if options.vardct else "manifest.json")).write_text(json.dumps(metadata, indent=2) + "\n")
    total = sum(p.stat().st_size for folder in (DEST, DEST.with_name("JxlConformance")) for p in folder.rglob("*") if p.is_file())
    if total > 100_000_000:
        raise SystemExit(f"JPEG XL resource budget exceeded: {total}")
    print(f"Generated {len(records)} cases; total JPEG XL resources: {total:,} bytes")


if __name__ == "__main__":
    main()
