#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
# Licensed under the Apache License, Version 2.0.
"""Record original HDR output vectors using explicitly enabled native references."""
import hashlib
import argparse
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "tests/CodeBrix.Imaging.Tests/SampleFiles/JxlSynthetic/Hdr"


def run(*args):
    result = subprocess.run(args, check=True, capture_output=True)
    if b"ignored" in result.stderr:
        raise ValueError(result.stderr.decode())
    return result


def read_pfm(path):
    with path.open("rb") as f:
        magic = f.readline().strip()
        width, height = map(int, f.readline().split())
        scale = float(f.readline())
        data = f.read()
    channels = 3 if magic == b"PF" else 1
    if magic not in (b"PF", b"Pf") or abs(scale) != 1:
        raise ValueError("Unexpected PFM header")
    if scale > 0:
        data = b"".join(data[i:i+4][::-1] for i in range(0, len(data), 4))
    stride = width * channels * 4
    return b"".join(data[y*stride:(y+1)*stride] for y in range(height-1, -1, -1))


def main():
    if os.environ.get("CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS") != "1":
        raise SystemExit("Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to invoke native reference tools.")
    parser = argparse.ArgumentParser()
    parser.add_argument("--cjxl", default="cjxl")
    parser.add_argument("--djxl", default="djxl")
    parser.add_argument("--tone-reference", required=True)
    options = parser.parse_args()
    records, files = [], {}
    with tempfile.TemporaryDirectory(prefix="codebrix-jxl-hdr-") as temporary:
        scratch = Path(temporary)
        width, height = 17, 5
        source = scratch / "input.ppm"
        source.write_bytes(f"P6\n{width} {height}\n255\n".encode() +
                           bytes(round((x / (width-1)) * 255) if y == 0 else (x * (37+c*11) + y * (59-c*7) + c*67) % 256
                                 for y in range(height) for x in range(width) for c in range(3)))
        for transfer in ("PeQ", "HLG"):
            for intensity in ((1000, 10000) if transfer == "PeQ" else (1000,)):
                for modular in (True, False):
                    name = f"{transfer.lower()}-{intensity}-{'modular' if modular else 'xyb'}"
                    encoding = f"RGB_D65_202_Rel_{transfer}"
                    encoded, decoded = scratch / "input.jxl", scratch / "output.pfm"
                    run(options.cjxl, str(source), str(encoded), "-d", "0" if modular else "1", "-m", "1" if modular else "0",
                        "-x", "color_space=" + encoding, f"--intensity_target={intensity}", "-e", "5", "--num_threads=1")
                    files[name + ".jxl"] = encoded.read_bytes()
                    refs = {}
                    for output, nits, color in (("srgb", 0, "RGB_D65_SRG_Rel_Lin"), ("sdr", 0, "RGB_D65_SRG_Rel_Lin"), ("original", 0, encoding)):
                        run(options.djxl, str(encoded), str(decoded), "--color_space=" + color, f"--display_nits={nits}", "--num_threads=1")
                        data = read_pfm(decoded)
                        if output == "sdr":
                            # Apply libjxl's independent scalar tone mapper to linear output.
                            # The decoder's combined CMS/tone stages handle non-XYB input differently.
                            linear, mapped = scratch / "linear.f32", scratch / "mapped.f32"
                            linear.write_bytes(data)
                            run(options.tone_reference, "pq" if transfer == "PeQ" else "hlg", str(intensity), "255", str(linear), str(mapped))
                            data = mapped.read_bytes()
                        elif output == "srgb":
                            # Evaluate the exact sRGB transfer equation. djxl's rational approximation
                            # has larger error for HDR values far outside 0..1.
                            values = []
                            for (v,) in struct.iter_unpack("<f", data):
                                a = abs(v)
                                values.append(math.copysign(12.92*a if a <= .0031308 else 1.055*a**(1/2.4)-.055, v))
                            data = struct.pack("<" + "f" * len(values), *values)
                        files[f"{name}.{output}.f32"] = data
                        refs[output] = hashlib.sha256(data).hexdigest()
                    records.append({"name": name, "width": width, "height": height, "encoding": encoding,
                                    "intensityTarget": intensity, "inputSha256": hashlib.sha256(encoded.read_bytes()).hexdigest(), "references": refs})
        manifest = {"license": "Apache-2.0", "copyright": "Copyright (c) 2026 Jeremy Ellis and contributors",
                    "format": "little-endian binary32 RGB, row-major top-to-bottom",
                    "displayReference": "djxl linear-sRGB output + libjxl scalar tone mapper + exact sRGB transfer equation",
                    "encoder": run(options.cjxl, "--version").stdout.decode().strip(),
                    "decoder": run(options.djxl, "--version").stdout.decode().strip(), "cases": records}
        files["manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
        current = sum(p.stat().st_size for folder in (DEST.parent, DEST.parent.with_name("JxlConformance"))
                      for p in folder.rglob("*") if p.is_file() and not (p.parent == DEST and p.name in files))
        total = current + sum(map(len, files.values()))
        if total > 100_000_000:
            raise SystemExit(f"JPEG XL resource budget exceeded before writing: {total}")
        DEST.mkdir(parents=True, exist_ok=True)
        for name, data in files.items():
            (DEST / name).write_bytes(data)
        print(f"Generated {len(records)} HDR cases with three outputs each; total JPEG XL resources: {total:,} bytes")


if __name__ == "__main__":
    main()
