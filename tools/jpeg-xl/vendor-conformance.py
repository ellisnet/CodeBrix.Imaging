#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
# Licensed under the Apache License, Version 2.0.
"""Copy reviewed JPEG XL test assets from the pinned upstream checkout.

This maintenance tool never downloads data. It excludes the bike/cafe inputs
and content-addressed full references, and checks the aggregate resource budget.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'tests/CodeBrix.Imaging.Tests/SampleFiles/JxlConformance'
CAP = 100_000_000
EXCLUDED = {'bike', 'cafe'}


def dimensions(path):
    data = path.read_bytes()
    if data.startswith(b'\x89PNG\r\n\x1a\n'):
        return struct.unpack('>II', data[16:24])
    if data.startswith((b'GIF87a', b'GIF89a')):
        return struct.unpack('<HH', data[6:10])
    if data.startswith((b'PF\n', b'Pf\n')):
        return tuple(map(int, data.splitlines()[1].split()))
    raise ValueError(f'Unrecognized preview: {path}')


def main():
    if os.environ.get('CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS') != '1':
        raise SystemExit('Set CODEBRIX_IMAGING_RUN_JXL_EXTERNAL_TESTS=1 to access external references.')
    native_tools = os.environ.get('CODEBRIX_IMAGING_JXL_NATIVE_TOOLS', '')
    if not native_tools or not Path(native_tools).is_absolute():
        raise SystemExit('Set CODEBRIX_IMAGING_JXL_NATIVE_TOOLS to the absolute directory of your pinned reference tools.')
    decoder = Path(native_tools) / ('djxl.exe' if os.name == 'nt' else 'djxl')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('checkout', type=Path)
    args = parser.parse_args()
    expected = json.loads((Path(__file__).with_name('upstream.json')).read_text())['conformance']['revision']
    actual = subprocess.check_output(['git', '-C', str(args.checkout), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != expected:
        raise SystemExit(f'Expected upstream revision {expected}, found {actual}')
    cases = args.checkout / 'testcases'
    files = [(args.checkout / 'LICENSE', Path('LICENSE')), (cases / 'README.md', Path('UPSTREAM-README.md'))]
    manifest = []
    for level in (5, 10):
        names = (cases / f'main_level{level}.txt').read_text().splitlines()
        for name in names:
            case = cases / name
            base = name.removesuffix('_5')
            settings = json.loads((case / 'test.json').read_text())
            preview = next((p for p in sorted((cases / base).glob('ref.*')) if p.suffix in ('.png', '.apng', '.gif', '.pfm')), None)
            if preview:
                width, height = dimensions(preview)
            else:
                # Maintenance-time reference only; the test suite uses the manifest.
                result = subprocess.run([str(decoder), str(case / 'input.jxl'), '--disable_output'],
                                        capture_output=True, text=True, check=True)
                match = re.search(r'(\d+) x (\d+),', result.stderr)
                if not match:
                    raise ValueError(f'No dimensions reported by djxl for {name}')
                width, height = map(int, match.groups())
            record = {'case': name, 'base': base, 'level': level, 'external': base in EXCLUDED,
                      'width': width, 'height': height, 'frames': len(settings['frames']),
                      'bitsPerSample': settings['bits_per_sample'][0],
                      'exponentBitsPerSample': settings['exp_bits_per_sample'][0],
                      'inputSha256': hashlib.sha256((case / 'input.jxl').read_bytes()).hexdigest()}
            manifest.append(record)
            if base in EXCLUDED:
                files.append((case / 'test.json', Path(name) / 'test.json'))
                continue
            for asset in case.iterdir():
                if asset.name in ('input.jxl', 'test.json', 'source') or (preview and asset.name == preview.name):
                    # Store shared Level 5 inputs/previews once, without checkout symlinks.
                    destination = Path(name if asset.name == 'test.json' else base) / asset.name
                    files.append((asset.resolve(strict=True), destination))
    unique = {destination: source for source, destination in files}
    existing = {p.relative_to(DEST): p.stat().st_size for p in DEST.rglob('*') if p.is_file()}
    for destination, source in unique.items():
        existing[destination] = source.stat().st_size
    serialized = json.dumps(manifest, indent=2) + '\n'
    existing[Path('manifest.json')] = len(serialized.encode())
    total = sum(existing.values()) + sum(p.stat().st_size for p in
        (DEST.parent / 'JxlSynthetic').rglob('*') if p.is_file())
    if total > CAP:
        raise SystemExit(f'Resources would exceed {CAP} bytes: {total}')
    for destination, source in unique.items():
        target = DEST / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    (DEST / 'manifest.json').write_text(serialized)
    print(f'{len(manifest)} level/case records; {len(unique)} files; {total:,} bytes.')


if __name__ == '__main__':
    main()
