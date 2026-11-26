"""Prepare actual architecture-specific runtimes. Never use global site-packages."""
import argparse
import hashlib
import json
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def prepare(arch):
    platform = 'win32' if arch == 'x86' else 'win_amd64'
    target = ROOT / 'artifacts/platforms' / arch
    target.mkdir(parents=True, exist_ok=True)
    wheels = ROOT / 'artifacts/dependencies' / arch
    subprocess.run([sys.executable, '-m', 'pip', 'download', '--disable-pip-version-check',
                    '--only-binary=:all:', '--platform', platform, '--python-version', '311',
                    '--implementation', 'cp', '--abi', 'cp311', '--abi', 'abi3', '--no-deps',
                    '--dest', str(wheels), 'frida==17.27.4', 'Pillow==13.0.0', 'imageio-ffmpeg==0.8.1'], check=True)
    deps = target / 'deps'
    deps.mkdir(exist_ok=True)
    for pattern in ('frida-17.27.4-*.whl', 'pillow-13.0.0-*.whl', 'imageio_ffmpeg-0.8.1-*.whl'):
        wheel, = wheels.glob(pattern)
        with zipfile.ZipFile(wheel) as archive:
            archive.extractall(deps)
    runtime = target / 'python'
    runtime.mkdir(exist_ok=True)
    suffix = 'win32' if arch == 'x86' else 'amd64'
    package = target / 'python-3.11.9-embed.zip'
    if not package.exists():
        request = urllib.request.Request(f'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-{suffix}.zip')
        with urllib.request.urlopen(request, timeout=120) as response:
            data = response.read()
        with zipfile.ZipFile(__import__('io').BytesIO(data)) as archive:
            assert archive.testzip() is None
        package.write_bytes(data)
    with zipfile.ZipFile(package) as archive:
        archive.extractall(runtime)
    (runtime / 'python311._pth').write_text('python311.zip\n.\n../../codecs/deps\n../../semantic\n', encoding='ascii')
    sdk = ROOT / 'dist/semantic/gemma2/webview2-1.0.4314.35.nupkg'
    with zipfile.ZipFile(sdk) as archive:
        (target / 'WebView2Loader.dll').write_bytes(archive.read(f'runtimes/win-{arch}/native/WebView2Loader.dll'))
    (target / 'architecture.json').write_text(json.dumps({'architecture': arch, 'python': '3.11.9',
        'pythonZipSha256': hashlib.sha256(package.read_bytes()).hexdigest()}), encoding='utf-8')
    print(target)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('architecture', choices=['x86', 'x64'])
    prepare(parser.parse_args().architecture)
